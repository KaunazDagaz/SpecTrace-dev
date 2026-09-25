using System.Text.Json;

namespace SpecTrace.Llm;

public sealed class CachingLlmClient : ILlmClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true, NewLine = "\n" };

    private readonly ILlmClient _inner;
    private readonly string _cacheDirectory;
    private readonly bool _offline;
    private readonly TimeProvider _timeProvider;

    public CachingLlmClient(
        ILlmClient inner,
        string cacheDirectory,
        bool offline,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        _inner = inner;
        _cacheDirectory = cacheDirectory;
        _offline = offline;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public const string OfflineVariable = "SPECTRACE_OFFLINE";

    public static bool IsOffline(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value, "0", StringComparison.Ordinal)
        && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = CacheKey.For(request);
        var path = PathFor(key);

        if (File.Exists(path))
        {
            var cached = await ReadAsync(path, cancellationToken).ConfigureAwait(false);

            return new LlmResponse(
                cached.Response.Text,
                cached.Response.InputTokens,
                cached.Response.OutputTokens,
                FromCache: true);
        }

        if (_offline)
        {
            throw new OfflineCacheMissException(key, path, request.Model, request.PromptSha256);
        }

        var response = await _inner.CompleteAsync(request, cancellationToken).ConfigureAwait(false);

        await WriteAsync(key, path, request, response, cancellationToken).ConfigureAwait(false);

        return response with { FromCache = false };
    }

    public string PathFor(string key) => Path.Combine(_cacheDirectory, $"{key}.json");

    private static async Task<CachedExchange> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);

            return await JsonSerializer
                .DeserializeAsync<CachedExchange>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new LlmResponseException($"Cache entry '{path}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new LlmResponseException(
                $"Cache entry '{path}' is not readable as a recorded exchange. It is committed "
                + "evidence, so this is a problem to fix rather than to refetch past.",
                exception);
        }
    }

    private async Task WriteAsync(
        string key,
        string path,
        LlmRequest request,
        LlmResponse response,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_cacheDirectory);

        var exchange = new CachedExchange(
            key,
            _timeProvider.GetUtcNow(),
            new CachedRequest(
                request.Model,
                request.Temperature,
                request.MaxOutputTokens,
                request.PromptSha256,
                request.SystemPrompt,
                request.UserPrompt,
                request.JsonSchema),
            new CachedResponse(response.Text, response.InputTokens, response.OutputTokens));

        var temporary = $"{path}.{Environment.ProcessId}.tmp";

        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer
                .SerializeAsync(stream, exchange, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
