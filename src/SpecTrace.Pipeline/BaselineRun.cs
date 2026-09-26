using System.Security.Cryptography;
using System.Text;
using SpecTrace.Core;
using SpecTrace.Llm;

namespace SpecTrace.Pipeline;

public static class BaselineRun
{
    public const string Arm = "baseline";

    public const int MaxOutputTokens = 65_536;

    public static LlmRequest RequestFor(string rawDocument, string model, PromptFile prompt)
    {
        ArgumentNullException.ThrowIfNull(rawDocument);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(prompt);

        return new LlmRequest(
            SystemPrompt: string.Empty,
            UserPrompt: $"{prompt.Text}\n{rawDocument}",
            Model: model,
            Temperature: LlmClientFactory.Temperature,
            MaxOutputTokens: MaxOutputTokens,
            JsonSchema: null,
            PromptSha256: prompt.Sha256);
    }

    public static async Task<BaselineRunResult> ExecuteAsync(
        string documentPath,
        ILlmClient client,
        string model,
        PromptFile prompt,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var startedAt = timeProvider.GetUtcNow();
        var raw = await File.ReadAllTextAsync(documentPath, cancellationToken).ConfigureAwait(false);
        var documentId = ExtractionRun.DocumentIdFor(documentPath);
        LlmResponse response;

        try
        {
            response = await client
                .CompleteAsync(RequestFor(raw, model, prompt), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OfflineCacheMissException miss)
        {
            throw miss.During($"the baseline call on {documentId}, prompt {prompt.Name}");
        }

        var runId = RunIdFor(documentId, raw, model, prompt);

        var manifest = new RunManifest(
            runId,
            documentId,
            LlmClientFactory.Provider,
            model,
            LlmClientFactory.Temperature,
            startedAt,
            PromptCount: 1,
            CacheHits: response.FromCache ? 1 : 0,
            response.InputTokens,
            response.OutputTokens,
            PipelineBuild.Version,
            PipelineBuild.GitSha);

        return new BaselineRunResult(
            runId,
            documentId,
            manifest,
            response,
            ScoredAnswer.Of(response.Text, NormalizedDocument.Create(raw)));
    }

    public static string RunIdFor(string documentId, string rawDocument, string model, PromptFile prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var material = $"{model}\n{prompt.Sha256}\n{rawDocument}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));

        return $"{documentId}-{Arm}-{hash[..12]}";
    }
}

public sealed record BaselineRunResult(
    string RunId,
    string DocumentId,
    RunManifest Manifest,
    LlmResponse Response,
    ScoredAnswer Answer);
