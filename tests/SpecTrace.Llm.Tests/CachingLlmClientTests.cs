using System.Text.Json;

namespace SpecTrace.Llm.Tests;

public sealed class CachingLlmClientTests
{
    private static LlmRequest Request(
        string systemPrompt = "system",
        string userPrompt = "user",
        string model = "gemini-3.5-flash",
        double temperature = 0,
        int maxOutputTokens = 1024,
        string? jsonSchema = "{\"type\":\"ARRAY\"}",
        string promptSha256 = "abc123") =>
        new(systemPrompt, userPrompt, model, temperature, maxOutputTokens, jsonSchema, promptSha256);

    [Fact]
    public async Task AnUnseenRequestIsForwardedToTheProviderAndRecordedOnDisk()
    {
        using var directory = new TemporaryDirectory();
        var inner = new RecordingLlmClient("[{\"quote\":\"x\"}]");
        var client = new CachingLlmClient(inner, directory.Path, offline: false);

        var response = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal(1, inner.Calls);
        Assert.False(response.FromCache);
        Assert.Equal("[{\"quote\":\"x\"}]", response.Text);
        Assert.Equal(1, directory.FileCount);
    }

    [Fact]
    public async Task ASecondIdenticalRequestIsServedFromDiskWithoutReachingTheProvider()
    {
        using var directory = new TemporaryDirectory();
        var inner = new RecordingLlmClient("recorded");
        var client = new CachingLlmClient(inner, directory.Path, offline: false);

        var first = await client.CompleteAsync(Request(), CancellationToken.None);
        var second = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal(1, inner.Calls);
        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(first.InputTokens, second.InputTokens);
        Assert.Equal(first.OutputTokens, second.OutputTokens);
    }

    [Fact]
    public async Task ARecordedExchangeKeepsTheRequestBesideTheAnswerSoTheCacheReadsAsEvidence()
    {
        using var directory = new TemporaryDirectory();
        var client = new CachingLlmClient(new RecordingLlmClient("answer"), directory.Path, offline: false);
        var request = Request(systemPrompt: "the system prompt", userPrompt: "the document");

        await client.CompleteAsync(request, CancellationToken.None);

        var exchange = JsonSerializer.Deserialize<CachedExchange>(
            await File.ReadAllTextAsync(
                client.PathFor(CacheKey.For(request)),
                CancellationToken.None))!;

        Assert.Equal("the system prompt", exchange.Request.SystemPrompt);
        Assert.Equal("the document", exchange.Request.UserPrompt);
        Assert.Equal("gemini-3.5-flash", exchange.Request.Model);
        Assert.Equal(0, exchange.Request.Temperature);
        Assert.Equal("answer", exchange.Response.Text);
    }

    [Fact]
    public async Task ARecordedExchangeNeverContainsAnythingResemblingACredential()
    {
        using var directory = new TemporaryDirectory();
        var client = new CachingLlmClient(new RecordingLlmClient(), directory.Path, offline: false);

        await client.CompleteAsync(Request(), CancellationToken.None);

        var written = await File.ReadAllTextAsync(
            Directory.GetFiles(directory.Path).Single(),
            CancellationToken.None);

        Assert.DoesNotContain("apiKey", written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("x-goog-api-key", written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GEMINI_API_KEY", written, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<string, LlmRequest> RequestsDifferingInOneField() => new()
    {
        { "model", Request(model: "gemini-3.8-flash") },
        { "system prompt", Request(systemPrompt: "a different system prompt") },
        { "user prompt", Request(userPrompt: "a different document") },
        { "temperature", Request(temperature: 0.7) },
        { "max output tokens", Request(maxOutputTokens: 2048) },
        { "response schema", Request(jsonSchema: "{\"type\":\"OBJECT\"}") },
        { "absent response schema", Request(jsonSchema: null) },
        { "prompt file hash", Request(promptSha256: "def456") },
    };

    [Theory]
    [MemberData(nameof(RequestsDifferingInOneField))]
    public void ChangingAnyFieldOfTheRequestChangesTheCacheKey(string field, LlmRequest changed)
    {
        Assert.NotEqual(CacheKey.For(Request()), CacheKey.For(changed));
        Assert.False(string.IsNullOrEmpty(field));
    }

    [Fact]
    public void TheSameRequestAlwaysProducesTheSameKey()
    {
        Assert.Equal(CacheKey.For(Request()), CacheKey.For(Request()));
    }

    [Fact]
    public void ACacheKeyIsALowercaseSha256Digest()
    {
        var key = CacheKey.For(Request());

        Assert.Equal(64, key.Length);
        Assert.All(key, character => Assert.Contains(character, "0123456789abcdef"));
    }

    [Fact]
    public async Task InOfflineModeACacheMissThrowsAndTheProviderIsNeverCalled()
    {
        using var directory = new TemporaryDirectory();
        var inner = new RecordingLlmClient();
        var client = new CachingLlmClient(inner, directory.Path, offline: true);

        var exception = await Assert.ThrowsAsync<OfflineCacheMissException>(
            () => client.CompleteAsync(Request(), CancellationToken.None));

        Assert.Equal(0, inner.Calls);
        Assert.Equal(CacheKey.For(Request()), exception.CacheKey);
        Assert.Equal(0, directory.FileCount);
    }

    [Fact]
    public async Task InOfflineModeARecordedExchangeIsStillServed()
    {
        using var directory = new TemporaryDirectory();
        var recorded = new CachingLlmClient(new RecordingLlmClient("from the cache"), directory.Path, offline: false);
        await recorded.CompleteAsync(Request(), CancellationToken.None);

        var offline = new CachingLlmClient(new UnreachableLlmClient(), directory.Path, offline: true);
        var response = await offline.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(response.FromCache);
        Assert.Equal("from the cache", response.Text);
    }

    [Fact]
    public async Task ACorruptCacheEntryIsReportedRatherThanQuietlyRefetched()
    {
        using var directory = new TemporaryDirectory();
        var inner = new RecordingLlmClient();
        var client = new CachingLlmClient(inner, directory.Path, offline: false);
        await File.WriteAllTextAsync(
            client.PathFor(CacheKey.For(Request())),
            "{ this is not json",
            CancellationToken.None);

        await Assert.ThrowsAsync<LlmResponseException>(
            () => client.CompleteAsync(Request(), CancellationToken.None));

        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public void OfflineModeIsOffWhenTheEnvironmentVariableIsUnsetAndOnWhenItIsSetToOne()
    {
        var original = Environment.GetEnvironmentVariable(CachingLlmClient.OfflineVariable);

        try
        {
            Environment.SetEnvironmentVariable(CachingLlmClient.OfflineVariable, null);
            Assert.False(CachingLlmClient.OfflineFromEnvironment());

            Environment.SetEnvironmentVariable(CachingLlmClient.OfflineVariable, "1");
            Assert.True(CachingLlmClient.OfflineFromEnvironment());

            Environment.SetEnvironmentVariable(CachingLlmClient.OfflineVariable, "0");
            Assert.False(CachingLlmClient.OfflineFromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable(CachingLlmClient.OfflineVariable, original);
        }
    }
}
