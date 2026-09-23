using System.Security.Cryptography;
using System.Text;
using SpecTrace.Core;
using SpecTrace.Llm;

namespace SpecTrace.Pipeline;

public static class ExtractionRun
{
    public static async Task<ExtractionRunResult> ExecuteAsync(
        string documentPath,
        ILlmClient client,
        string model,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var raw = await File.ReadAllTextAsync(documentPath, cancellationToken).ConfigureAwait(false);
        var documentId = Path.GetFileNameWithoutExtension(documentPath);

        var extraction = await new RequirementExtractor(client, model)
            .ExtractAsync(documentId, raw, cancellationToken)
            .ConfigureAwait(false);

        var outcome = new QuoteVerifier(
                documentId,
                NormalizedDocument.Create(raw),
                SectionIndex.Build(raw))
            .Verify(extraction.Candidates);

        return new ExtractionRunResult(
            RunIdFor(documentId, raw, model),
            documentId,
            extraction.Candidates.Count,
            outcome,
            extraction.Response);
    }

    public static string RunIdFor(string documentId, string rawDocument, string model)
    {
        var material = $"{model}\n{PromptFile.Extraction.Sha256}\n{rawDocument}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));

        return $"{documentId}-{hash[..12]}";
    }
}

public sealed record ExtractionRunResult(
    string RunId,
    string DocumentId,
    int CandidateCount,
    VerificationOutcome Outcome,
    LlmResponse Response);
