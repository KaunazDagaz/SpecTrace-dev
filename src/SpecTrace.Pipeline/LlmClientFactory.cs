using SpecTrace.Llm;

namespace SpecTrace.Pipeline;

public static class LlmClientFactory
{
    public const string DefaultModel = "gemini-3.5-flash";

    private const string OfflinePlaceholderKey = "offline-no-key-is-used";

    public static ILlmClient Create(
        HttpClient httpClient,
        string cacheDirectory,
        bool offline,
        string? apiKey)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        var key = apiKey
            ?? (offline
                ? OfflinePlaceholderKey
                : throw new InvalidOperationException(
                    $"{GeminiLlmClient.ApiKeyVariable} is not set. Set it to run against the "
                    + $"provider, or set {CachingLlmClient.OfflineVariable}=1 to replay from the "
                    + "committed cache with no key."));

        return new CachingLlmClient(
            new RateLimitedLlmClient(new GeminiLlmClient(httpClient, key)),
            cacheDirectory,
            offline);
    }
}
