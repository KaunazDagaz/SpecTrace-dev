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
        PromptFile prompt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(prompt);

        var raw = await File.ReadAllTextAsync(documentPath, cancellationToken).ConfigureAwait(false);

        return await ExecuteAsync(DocumentIdFor(documentPath), raw, client, model, prompt, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<ExtractionRunResult> ExecuteAsync(
        string documentId,
        string raw,
        ILlmClient client,
        string model,
        PromptFile prompt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(prompt);

        var extraction = await new RequirementExtractor(client, model, prompt: prompt)
            .ExtractAsync(documentId, raw, cancellationToken)
            .ConfigureAwait(false);

        var outcome = new QuoteVerifier(
                documentId,
                NormalizedDocument.Create(raw),
                SectionIndex.Build(raw))
            .Verify(extraction.Candidates);

        return new ExtractionRunResult(
            RunIdFor(documentId, raw, model, prompt),
            documentId,
            extraction.Candidates,
            outcome,
            extraction.Response);
    }

    public static string DocumentIdFor(string documentPath) => Path.GetFileNameWithoutExtension(documentPath);

    public static string RunIdFor(string documentId, string rawDocument, string model, PromptFile prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var material = $"{model}\n{prompt.Sha256}\n{rawDocument}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));

        return $"{documentId}-{hash[..12]}";
    }
}

public sealed record ExtractionRunResult(
    string RunId,
    string DocumentId,
    IReadOnlyList<CandidateRequirement> Candidates,
    VerificationOutcome Outcome,
    LlmResponse Response);
