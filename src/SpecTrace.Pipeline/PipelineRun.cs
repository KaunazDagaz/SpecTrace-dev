using System.Security.Cryptography;
using System.Text;
using SpecTrace.Core;
using SpecTrace.Llm;

namespace SpecTrace.Pipeline;

public static class PipelineRun
{
    private static readonly HumanCoverageDecision[] NoHumanDecisions = [];

    public static async Task<PipelineRunResult> ExecuteAsync(
        string documentPath,
        ILlmClient client,
        string model,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var raw = await File.ReadAllTextAsync(documentPath, cancellationToken).ConfigureAwait(false);
        var documentId = ExtractionRun.DocumentIdFor(documentPath);

        var extraction = await ExtractionRun
            .ExecuteAsync(documentId, raw, client, model, cancellationToken)
            .ConfigureAwait(false);

        var register = extraction.Outcome.Register;
        var generator = new TestCaseGenerator(client, model);
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

        return new PipelineRunResult(
            RunIdFor(documentId, raw, model),
            documentId,
            extraction,
            generations,
            cases,
            decisionQueue,
            NoHumanDecisions,
            TraceabilityMatrix.Build(register, cases, NoHumanDecisions));
    }

    public static string RunIdFor(string documentId, string rawDocument, string model)
    {
        var material =
            $"{model}\n{PromptFile.Extraction.Sha256}\n{PromptFile.Generation.Sha256}\n{rawDocument}";
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
    ExtractionRunResult Extraction,
    IReadOnlyList<Generation> Generations,
    IReadOnlyList<TestCase> Cases,
    IReadOnlyList<QueuedDecision> DecisionQueue,
    IReadOnlyList<HumanCoverageDecision> HumanDecisions,
    TraceabilityMatrix Matrix)
{
    public IReadOnlyList<Requirement> Register => Extraction.Outcome.Register;
}
