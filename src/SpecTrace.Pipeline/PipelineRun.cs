using System.Security.Cryptography;
using System.Text;
using SpecTrace.Core;
using SpecTrace.Llm;

namespace SpecTrace.Pipeline;

public static class PipelineRun
{
    private static readonly HumanCoverageDecision[] NoHumanDecisions = [];

    public static Task<PipelineRunResult> ExecuteAsync(
        string documentPath,
        ILlmClient client,
        string model,
        CancellationToken cancellationToken) =>
        ExecuteAsync(documentPath, client, model, PromptSet.Embedded, TimeProvider.System, cancellationToken);

    public static async Task<PipelineRunResult> ExecuteAsync(
        string documentPath,
        ILlmClient client,
        string model,
        PromptSet prompts,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var startedAt = timeProvider.GetUtcNow();
        var raw = await File.ReadAllTextAsync(documentPath, cancellationToken).ConfigureAwait(false);
        var documentId = ExtractionRun.DocumentIdFor(documentPath);

        var extraction = await ExtractionRun
            .ExecuteAsync(documentId, raw, client, model, prompts.Extraction, cancellationToken)
            .ConfigureAwait(false);

        var register = extraction.Outcome.Register;
        var generator = new TestCaseGenerator(client, model, prompt: prompts.Generation);
        var generations = new List<Generation>();
        var cases = new List<TestCase>();
        var decisionQueue = new List<QueuedDecision>(extraction.Outcome.Decisions);

        foreach (var requirement in InDocumentOrder(register))
        {
            if (requirement.Testability != Testability.Testable)
            {
                decisionQueue.Add(QueuedDecision.ModelFlagged(requirement));
                continue;
            }

            var generation = await generator.GenerateAsync(requirement, cancellationToken).ConfigureAwait(false);
            generations.Add(generation);
            cases.AddRange(generation.Cases);

            if (generation.BlockedReason is { } blockedReason)
            {
                decisionQueue.Add(QueuedDecision.Blocked(requirement, blockedReason));
            }
        }

        var runId = RunIdFor(documentId, raw, model, prompts);
        var responses = generations.Select(generation => generation.Response).Prepend(extraction.Response).ToList();

        var manifest = new RunManifest(
            runId,
            documentId,
            LlmClientFactory.Provider,
            model,
            LlmClientFactory.Temperature,
            startedAt,
            responses.Count,
            responses.Count(response => response.FromCache),
            responses.Sum(response => response.InputTokens),
            responses.Sum(response => response.OutputTokens),
            PipelineBuild.Version,
            PipelineBuild.GitSha);

        return new PipelineRunResult(
            runId,
            documentId,
            manifest,
            extraction,
            generations,
            cases,
            decisionQueue,
            NoHumanDecisions,
            TraceabilityMatrix.Build(register, cases, NoHumanDecisions));
    }

    public static string RunIdFor(string documentId, string rawDocument, string model, PromptSet prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);

        var material =
            $"{model}\n{prompts.Extraction.Sha256}\n{prompts.Generation.Sha256}\n{rawDocument}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));

        return $"{documentId}-{hash[..12]}";
    }

    private static IEnumerable<Requirement> InDocumentOrder(IEnumerable<Requirement> register) =>
        register
            .OrderBy(requirement => requirement.Span.Start)
            .ThenBy(requirement => requirement.Span.End)
            .ThenBy(requirement => requirement.Id, StringComparer.Ordinal);
}

public sealed record PipelineRunResult(
    string RunId,
    string DocumentId,
    RunManifest Manifest,
    ExtractionRunResult Extraction,
    IReadOnlyList<Generation> Generations,
    IReadOnlyList<TestCase> Cases,
    IReadOnlyList<QueuedDecision> DecisionQueue,
    IReadOnlyList<HumanCoverageDecision> HumanDecisions,
    TraceabilityMatrix Matrix)
{
    public IReadOnlyList<Requirement> Register => Extraction.Outcome.Register;
}
