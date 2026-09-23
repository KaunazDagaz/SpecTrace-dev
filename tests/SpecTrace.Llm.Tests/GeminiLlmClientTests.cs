using System.Net;
using System.Text.Json;

namespace SpecTrace.Llm.Tests;

public sealed class GeminiLlmClientTests
{
    private const string SuccessBody = """
        {
          "candidates": [
            {
              "content": { "parts": [ { "text": "[{\"quote\":\"x\"}]" } ], "role": "model" },
              "finishReason": "STOP"
            }
          ],
          "usageMetadata": { "promptTokenCount": 5621, "candidatesTokenCount": 842 },
          "modelVersion": "gemini-3.5-flash"
        }
        """;

    private static LlmRequest Request(string? schema = "{\"type\":\"ARRAY\"}", int maxOutputTokens = 4096) =>
        new("the system prompt", "the document", "gemini-3.5-flash", 0, maxOutputTokens, schema, "hash");

    private static (GeminiLlmClient Client, StubHttpMessageHandler Handler) ClientFor(
        params HttpResponseMessage[] responses)
    {
        var handler = new StubHttpMessageHandler(responses);
        var httpClient = new HttpClient(handler) { BaseAddress = GeminiLlmClient.DefaultBaseAddress };

        return (new GeminiLlmClient(httpClient, "test-key"), handler);
    }

    [Fact]
    public async Task TheRequestGoesToTheGenerateContentEndpointForTheRequestedModel()
    {
        var (client, handler) = ClientFor(StubHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody));

        await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash:generateContent",
            handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task TheKeyTravelsInTheHeaderAndNeverInTheUrl()
    {
        var (client, handler) = ClientFor(StubHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody));

        await client.CompleteAsync(Request(), CancellationToken.None);

        var sent = handler.Requests.Single();

        Assert.Equal("test-key", sent.Headers.GetValues("x-goog-api-key").Single());

        Assert.DoesNotContain("test-key", sent.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("key=", sent.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBodyCarriesTheSystemPromptTheDocumentTemperatureZeroAndTheSchema()
    {
        var (client, handler) = ClientFor(StubHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody));

        await client.CompleteAsync(Request(), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.RequestBodies.Single());
        var root = body.RootElement;

        Assert.Equal(
            "the system prompt",
            root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(
            "the document",
            root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());

        var config = root.GetProperty("generationConfig");
        Assert.Equal(0, config.GetProperty("temperature").GetDouble());
        Assert.Equal(4096, config.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
        Assert.Equal("ARRAY", config.GetProperty("responseSchema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ARequestWithNoSchemaAsksForNoJsonMimeType()
    {
        var (client, handler) = ClientFor(StubHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody));

        await client.CompleteAsync(Request(schema: null), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.RequestBodies.Single());
        var config = body.RootElement.GetProperty("generationConfig");

        Assert.False(config.TryGetProperty("responseSchema", out _));
        Assert.False(config.TryGetProperty("responseMimeType", out _));
    }

    [Fact]
    public async Task TheAnswerTextAndTheProvidersOwnTokenCountsAreReadBack()
    {
        var (client, _) = ClientFor(StubHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody));

        var response = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal("[{\"quote\":\"x\"}]", response.Text);
        Assert.Equal(5621, response.InputTokens);
        Assert.Equal(842, response.OutputTokens);
        Assert.False(response.FromCache);
    }

    [Fact]
    public async Task AnAnswerSplitAcrossSeveralPartsIsJoinedInOrder()
    {
        const string Split = """
            {
              "candidates": [
                {
                  "content": { "parts": [ { "text": "[{\"quote\":" }, { "text": "\"x\"}]" } ] },
                  "finishReason": "STOP"
                }
              ],
              "usageMetadata": { "promptTokenCount": 1, "candidatesTokenCount": 2 }
            }
            """;
        var (client, _) = ClientFor(StubHttpMessageHandler.Json(HttpStatusCode.OK, Split));

        var response = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal("[{\"quote\":\"x\"}]", response.Text);
    }

    [Fact]
    public async Task AnAnswerCutShortByTheTokenCeilingIsRejectedRatherThanReturnedHalfRead()
    {
        const string Truncated = """
            {
              "candidates": [
                { "content": { "parts": [ { "text": "[{\"quote\":\"x" } ] }, "finishReason": "MAX_TOKENS" }
              ],
              "usageMetadata": { "promptTokenCount": 1, "candidatesTokenCount": 4096 }
            }
            """;
        var (client, _) = ClientFor(StubHttpMessageHandler.Json(HttpStatusCode.OK, Truncated));

        var exception = await Assert.ThrowsAsync<LlmResponseException>(
            () => client.CompleteAsync(Request(), CancellationToken.None));

        Assert.Contains("MAX_TOKENS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AResponseWithNoCandidatesIsReportedRatherThanTreatedAsAnEmptyAnswer()
    {
        var (client, _) = ClientFor(
            StubHttpMessageHandler.Json(HttpStatusCode.OK, """{ "candidates": [] }"""));

        await Assert.ThrowsAsync<LlmResponseException>(
            () => client.CompleteAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task ABodyThatIsNotJsonIsReportedAsSuch()
    {
        var (client, _) = ClientFor(StubHttpMessageHandler.Json(HttpStatusCode.OK, "<html>nope</html>"));

        await Assert.ThrowsAsync<LlmResponseException>(
            () => client.CompleteAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task ARejectedKeySaysSoRatherThanFailingOpaquely()
    {
        var (client, _) = ClientFor(StubHttpMessageHandler.Json(
            HttpStatusCode.Unauthorized,
            """{ "error": { "code": 401, "message": "API key not valid" } }"""));

        var exception = await Assert.ThrowsAsync<LlmAuthenticationException>(
            () => client.CompleteAsync(Request(), CancellationToken.None));

        Assert.Contains("GEMINI_API_KEY", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnHttp429BecomesARateLimitFailureCarryingAnyRetryAfterHint()
    {
        var response = StubHttpMessageHandler.Json(
            HttpStatusCode.TooManyRequests,
            """{ "error": { "code": 429, "status": "RESOURCE_EXHAUSTED" } }""");
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromSeconds(30));
        var (client, _) = ClientFor(response);

        var exception = await Assert.ThrowsAsync<LlmRateLimitException>(
            () => client.CompleteAsync(Request(), CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
    }

    [Fact]
    public async Task AQuotaErrorsRetryHintIsReadFromTheBodyBecauseGeminiSendsNoRetryAfterHeader()
    {
        const string Quota = """
            {
              "error": {
                "code": 429,
                "message": "You exceeded your current quota. Quota exceeded for metric: generate_content_free_tier_requests, limit: 20, model: gemini-3.5-flash. Please retry in 25.775797133s.",
                "status": "RESOURCE_EXHAUSTED",
                "details": [
                  { "@type": "type.googleapis.com/google.rpc.RetryInfo", "retryDelay": "25.775797133s" }
                ]
              }
            }
            """;
        var (client, _) = ClientFor(StubHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, Quota));

        var exception = await Assert.ThrowsAsync<LlmRateLimitException>(
            () => client.CompleteAsync(Request(), CancellationToken.None));

        Assert.NotNull(exception.RetryAfter);
        Assert.Equal(25.775797133, exception.RetryAfter!.Value.TotalSeconds, precision: 6);
    }

    private const string DailyQuotaExhausted = """
        {
          "error": {
            "code": 429,
            "message": "You exceeded your current quota. Quota exceeded for metric: generativelanguage.googleapis.com/generate_content_free_tier_requests, limit: 500, model: gemini-3.5-flash-lite. Please retry in 25s.",
            "status": "RESOURCE_EXHAUSTED",
            "details": [
              {
                "@type": "type.googleapis.com/google.rpc.QuotaFailure",
                "violations": [
                  {
                    "quotaMetric": "generativelanguage.googleapis.com/generate_content_free_tier_requests",
                    "quotaId": "GenerateRequestsPerDayPerProjectPerModel-FreeTier",
                    "quotaDimensions": { "location": "global", "model": "gemini-3.5-flash-lite" },
                    "quotaValue": "500"
                  }
                ]
              },
              { "@type": "type.googleapis.com/google.rpc.RetryInfo", "retryDelay": "25s" }
            ]
          }
        }
        """;

    private const string PerMinuteLimitReached = """
        {
          "error": {
            "code": 429,
            "message": "You exceeded your current quota. Quota exceeded for metric: generativelanguage.googleapis.com/generate_content_free_tier_requests, limit: 10, model: gemini-3.5-flash-lite. Please retry in 7s.",
            "status": "RESOURCE_EXHAUSTED",
            "details": [
              {
                "@type": "type.googleapis.com/google.rpc.QuotaFailure",
                "violations": [
                  {
                    "quotaMetric": "generativelanguage.googleapis.com/generate_content_free_tier_requests",
                    "quotaId": "GenerateRequestsPerMinutePerProjectPerModel-FreeTier",
                    "quotaDimensions": { "location": "global", "model": "gemini-3.5-flash-lite" },
                    "quotaValue": "10"
                  }
                ]
              },
              { "@type": "type.googleapis.com/google.rpc.RetryInfo", "retryDelay": "7s" }
            ]
          }
        }
        """;

    private static RateLimitedLlmClient RateLimited(GeminiLlmClient client, DelayRecorder recorder) =>
        new(
            client,
            requestsPerMinute: 60_000,
            maximumAttempts: 6,
            timeProvider: recorder.Clock,
            delayAsync: recorder.RecordAsync);

    [Fact]
    public async Task AnExhaustedDailyQuotaIsReportedAsExhaustedEvenWhenTheBodyAlsoCarriesAShortRetryHint()
    {
        var (client, _) = ClientFor(StubHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, DailyQuotaExhausted));

        var exception = await Assert.ThrowsAsync<LlmQuotaExhaustedException>(
            () => client.CompleteAsync(Request(), CancellationToken.None));

        Assert.IsNotType<LlmRetryableException>(exception, exactMatch: false);
        Assert.Equal("GenerateRequestsPerDayPerProjectPerModel-FreeTier", exception.QuotaId);
        Assert.Equal("500", exception.Limit);
        Assert.Contains("gemini-3.5-flash", exception.Message, StringComparison.Ordinal);
        Assert.Contains("midnight Pacific", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SPECTRACE_OFFLINE", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APerMinuteLimitIsReportedAsARetryableRateLimitWithItsRetryHint()
    {
        var (client, _) = ClientFor(StubHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, PerMinuteLimitReached));

        var exception = await Assert.ThrowsAsync<LlmRateLimitException>(
            () => client.CompleteAsync(Request(), CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(7), exception.RetryAfter);
    }

    [Fact]
    public async Task AnExhaustedDailyQuotaReachesTheProviderOnceAndIsNeverRetried()
    {
        var (client, handler) = ClientFor(
            StubHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, DailyQuotaExhausted),
            StubHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody));
        var recorder = new DelayRecorder();

        await Assert.ThrowsAsync<LlmQuotaExhaustedException>(
            () => RateLimited(client, recorder).CompleteAsync(Request(), CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.Empty(recorder.Delays);
    }

    [Fact]
    public async Task APerMinuteLimitIsRetriedAfterItsHintAndTheCallThenSucceeds()
    {
        var (client, handler) = ClientFor(
            StubHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, PerMinuteLimitReached),
            StubHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, PerMinuteLimitReached),
            StubHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody));
        var recorder = new DelayRecorder();

        var response = await RateLimited(client, recorder).CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal("[{\"quote\":\"x\"}]", response.Text);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7)], recorder.Delays);
    }

    [Fact]
    public async Task A429WithNoQuotaDetailsIsTreatedAsARetryableRateLimit()
    {
        var (client, _) = ClientFor(StubHttpMessageHandler.Json(
            HttpStatusCode.TooManyRequests,
            """{ "error": { "code": 429, "status": "RESOURCE_EXHAUSTED" } }"""));

        await Assert.ThrowsAsync<LlmRateLimitException>(
            () => client.CompleteAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task AnHttp503BecomesATransientFailureBecauseTheProviderCallsItTemporary()
    {
        var (client, _) = ClientFor(StubHttpMessageHandler.Json(
            HttpStatusCode.ServiceUnavailable,
            """{ "error": { "code": 503, "status": "UNAVAILABLE" } }"""));

        await Assert.ThrowsAsync<LlmTransientException>(
            () => client.CompleteAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task AConnectionThatNeverBecomesAnHttpStatusIsStillTreatedAsTransient()
    {
        using var httpClient = new HttpClient(new OfflineHttpMessageHandler())
        {
            BaseAddress = GeminiLlmClient.DefaultBaseAddress,
        };
        var client = new GeminiLlmClient(httpClient, "test-key");

        await Assert.ThrowsAsync<LlmTransientException>(
            () => client.CompleteAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task ACancellationTheCallerAskedForIsNotDisguisedAsAProviderFailure()
    {
        var (client, _) = ClientFor(StubHttpMessageHandler.Json(HttpStatusCode.OK, SuccessBody));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CompleteAsync(Request(), source.Token));
    }

    [Fact]
    public async Task AnUnexpectedStatusIsNotSilentlyRetryable()
    {
        var (client, _) = ClientFor(StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{}"));

        var exception = await Assert.ThrowsAsync<LlmResponseException>(
            () => client.CompleteAsync(Request(), CancellationToken.None));

        Assert.IsNotType<LlmRetryableException>(exception, exactMatch: false);
    }
}
