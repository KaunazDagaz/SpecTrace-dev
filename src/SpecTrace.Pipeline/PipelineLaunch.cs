using SpecTrace.Llm;

namespace SpecTrace.Pipeline;

public delegate ILlmClient ModelClientFactory(string cacheDirectory, bool offline);

public static class PipelineLaunch
{
    public const string OfflineVariable = CachingLlmClient.OfflineVariable;

    public const string ApiKeyVariable = GeminiLlmClient.ApiKeyVariable;

    public const string RunsDirectory = "runs";

    public static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(3);

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

    public static ModelClientFactory ModelClients(HttpClient httpClient, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(environment);

        return (cacheDirectory, offline) => LlmClientFactory.Create(
            httpClient,
            cacheDirectory,
            offline,
            GeminiLlmClient.ApiKeyFrom(environment(ApiKeyVariable)));
    }

    public static async Task<(PipelineRunResult Result, string OutputDirectory)> RunAsync(
        string documentPath,
        ILlmClient client,
        string model,
        PromptSet prompts,
        TimeProvider timeProvider,
        string? outputDirectory,
        CancellationToken cancellationToken)
    {
        var result = await PipelineRun
            .ExecuteAsync(documentPath, client, model, prompts, timeProvider, cancellationToken)
            .ConfigureAwait(false);

        var directory = outputDirectory ?? Path.Combine(RunsDirectory, result.RunId);

        await RunArtifacts.WriteRunAsync(result, directory, cancellationToken).ConfigureAwait(false);

        return (result, directory);
    }
}
