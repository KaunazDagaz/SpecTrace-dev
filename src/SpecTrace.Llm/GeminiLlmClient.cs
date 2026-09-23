using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SpecTrace.Llm;

public sealed class GeminiLlmClient : ILlmClient
{
    public static readonly Uri DefaultBaseAddress = new("https://generativelanguage.googleapis.com/");

    public const string ApiKeyVariable = "GEMINI_API_KEY";

    private const string ApiKeyHeader = "x-goog-api-key";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public GeminiLlmClient(HttpClient httpClient, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _httpClient = httpClient;
        _apiKey = apiKey;

        _httpClient.BaseAddress ??= DefaultBaseAddress;
    }

    public static string? ApiKeyFromEnvironment()
    {
        var key = Environment.GetEnvironmentVariable(ApiKeyVariable);

        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{request.Model}:generateContent")
        {
            Content = new StringContent(BuildBody(request), Encoding.UTF8, "application/json"),
        };

        message.Headers.TryAddWithoutValidation(ApiKeyHeader, _apiKey);

        HttpResponseMessage response;

        try
        {
            response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new LlmTransientException(
                $"The request to Gemini did not complete: {exception.Message}",
                retryAfter: null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmTransientException(
                $"The request to Gemini timed out after {_httpClient.Timeout}.",
                retryAfter: null);
        }

        using var completed = response;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw Failure(response, body);
        }

        return Parse(body, request);
    }

    private static string BuildBody(LlmRequest request)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("systemInstruction");
            writer.WriteStartArray("parts");
            writer.WriteStartObject();
            writer.WriteString("text", request.SystemPrompt);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WriteStartArray("contents");
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteStartArray("parts");
            writer.WriteStartObject();
            writer.WriteString("text", request.UserPrompt);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteStartObject("generationConfig");
            writer.WriteNumber("temperature", request.Temperature);
            writer.WriteNumber("maxOutputTokens", request.MaxOutputTokens);

            if (request.JsonSchema is not null)
            {
                writer.WriteString("responseMimeType", "application/json");
                writer.WritePropertyName("responseSchema");
                writer.WriteRawValue(request.JsonSchema);
            }

            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static LlmResponse Parse(string body, LlmRequest request)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new LlmResponseException("Gemini returned a body that is not JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
            {
                throw new LlmResponseException(
                    $"Gemini returned no candidates for model '{request.Model}'. Body: {Truncate(body)}");
            }

            var candidate = candidates[0];

            if (candidate.TryGetProperty("finishReason", out var finishReason)
                && finishReason.GetString() is { } reason
                && !string.Equals(reason, "STOP", StringComparison.Ordinal))
            {
                throw new LlmResponseException(
                    $"Gemini stopped for reason '{reason}', not 'STOP', so the response is "
                    + $"incomplete and is not usable. Raise MaxOutputTokens (currently "
                    + $"{request.MaxOutputTokens}) if the reason is MAX_TOKENS.");
            }

            var text = new StringBuilder();

            if (candidate.TryGetProperty("content", out var content)
                && content.TryGetProperty("parts", out var parts)
                && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var partText))
                    {
                        text.Append(partText.GetString());
                    }
                }
            }

            if (text.Length == 0)
            {
                throw new LlmResponseException(
                    $"Gemini returned a candidate with no text. Body: {Truncate(body)}");
            }

            var (inputTokens, outputTokens) = ReadUsage(root);

            return new LlmResponse(text.ToString(), inputTokens, outputTokens, FromCache: false);
        }
    }

    private static (int InputTokens, int OutputTokens) ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage))
        {
            return (0, 0);
        }

        var input = usage.TryGetProperty("promptTokenCount", out var prompt) ? prompt.GetInt32() : 0;
        var output = usage.TryGetProperty("candidatesTokenCount", out var candidate)
            ? candidate.GetInt32()
            : 0;

        return (input, output);
    }

    private static LlmException Failure(HttpResponseMessage response, string body)
    {
        var retryAfter = RetryAfterOf(response.Headers.RetryAfter) ?? RetryDelayInBody(body);
        var status = (int)response.StatusCode;
        var detail = Truncate(body);

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new LlmAuthenticationException(
                    $"Gemini rejected the credentials (HTTP {status}). Check that "
                    + $"{ApiKeyVariable} holds a current AI Studio key: keys issued before "
                    + "28 May 2026 are the older 'standard' type, which Google began rejecting "
                    + $"in September 2026. Body: {detail}"),

            HttpStatusCode.TooManyRequests =>
                new LlmRateLimitException(
                    $"Gemini rate limit reached (HTTP 429). Free-tier limits are per project and "
                    + $"visible only in AI Studio. Body: {detail}",
                    retryAfter),

            >= HttpStatusCode.InternalServerError =>
                new LlmTransientException(
                    $"Gemini is temporarily unavailable (HTTP {status}). Body: {detail}",
                    retryAfter),

            _ => new LlmResponseException($"Gemini returned HTTP {status}. Body: {detail}"),
        };
    }

    private static TimeSpan? RetryAfterOf(RetryConditionHeaderValue? header)
    {
        if (header is null)
        {
            return null;
        }

        if (header.Delta is { } delta)
        {
            return delta;
        }

        return header.Date is { } date && date > DateTimeOffset.UtcNow
            ? date - DateTimeOffset.UtcNow
            : null;
    }

    private static TimeSpan? RetryDelayInBody(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("error", out var error)
                || !error.TryGetProperty("details", out var details)
                || details.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var detail in details.EnumerateArray())
            {
                if (detail.TryGetProperty("@type", out var type)
                    && type.GetString() is { } typeName
                    && typeName.EndsWith("RetryInfo", StringComparison.Ordinal)
                    && detail.TryGetProperty("retryDelay", out var delay)
                    && ParseDuration(delay.GetString()) is { } parsed)
                {
                    return parsed;
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static TimeSpan? ParseDuration(string? value) =>
        value is not null
        && value.EndsWith('s')
        && double.TryParse(
            value.AsSpan(0, value.Length - 1),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;

    private static string Truncate(string body) =>
        body.Length <= 500 ? body : string.Concat(body.AsSpan(0, 500), "…");
}
