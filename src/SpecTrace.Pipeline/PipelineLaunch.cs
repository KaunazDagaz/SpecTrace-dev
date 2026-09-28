using SpecTrace.Llm;

namespace SpecTrace.Pipeline;

public static class PipelineLaunch
{
    public const string OfflineVariable = CachingLlmClient.OfflineVariable;

    public const string ApiKeyVariable = GeminiLlmClient.ApiKeyVariable;

    public static bool IsOffline(Func<string, string?> environment, bool offlineFlag)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return offlineFlag || CachingLlmClient.IsOffline(environment(OfflineVariable));
    }

    public static bool HasApiKey(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return GeminiLlmClient.ApiKeyFrom(environment(ApiKeyVariable)) is not null;
    }
}
