namespace SpecTrace.Llm.Tests;

public sealed class LiveGeminiTests
{
    private const string Model = "gemini-3.5-flash-lite";

    private static LlmRequest SmokeRequest() =>
        new(
            SystemPrompt: "You return JSON only. No prose, no code fences.",
            UserPrompt: "Return the modality of this sentence: The client MUST retry.",
            Model: Model,
            Temperature: 0,
            MaxOutputTokens: 256,
            JsonSchema: """
                {"type":"OBJECT","properties":{"modality":{"type":"STRING",
                "enum":["MUST","MUST_NOT","SHOULD","SHOULD_NOT","MAY"]}},"required":["modality"]}
                """,
            PromptSha256: "live-smoke");

    [RequiresGeminiKeyFact]
    public async Task OneRealCallIsAnsweredAndThenReplaysFromTheCacheWithTheNetworkGone()
    {
        using var directory = new TemporaryDirectory();
        var request = SmokeRequest();

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var live = new CachingLlmClient(
            new RateLimitedLlmClient(
                new GeminiLlmClient(httpClient, GeminiLlmClient.ApiKeyFromEnvironment()!),
                maximumAttempts: 5,
                firstBackoff: TimeSpan.FromSeconds(2)),
            directory.Path,
            offline: false);

        var answered = await live.CompleteAsync(request, CancellationToken.None);

        Assert.False(answered.FromCache);
        Assert.Contains("MUST", answered.Text, StringComparison.Ordinal);
        Assert.True(answered.InputTokens > 0, "The provider should report prompt tokens.");
        Assert.True(File.Exists(live.PathFor(CacheKey.For(request))), "The exchange should be recorded.");

        using var severed = new HttpClient(new OfflineHttpMessageHandler());
        var replay = new CachingLlmClient(
            new RateLimitedLlmClient(new GeminiLlmClient(severed, "a key that is never used")),
            directory.Path,
            offline: true);

        var replayed = await replay.CompleteAsync(request, CancellationToken.None);

        Assert.True(replayed.FromCache);
        Assert.Equal(answered.Text, replayed.Text);
        Assert.Equal(answered.InputTokens, replayed.InputTokens);
        Assert.Equal(answered.OutputTokens, replayed.OutputTokens);
    }
}
