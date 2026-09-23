using System.Security.Cryptography;
using System.Text;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public sealed class QuoteVerifier
{
    private readonly NormalizedDocument _document;
    private readonly SectionIndex _sections;
    private readonly string _documentId;

    public QuoteVerifier(string documentId, NormalizedDocument document, SectionIndex sections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sections);

        _documentId = documentId;
        _document = document;
        _sections = sections;
    }

    public VerificationOutcome Verify(IReadOnlyList<CandidateRequirement> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var register = new List<Requirement>();
        var rejected = new List<RejectedQuote>();
        var decisions = new List<DecisionQueueItem>();

        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var queued = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            var resolution = _document.Resolve(candidate.Quote);

            switch (resolution.Verification)
            {
                case Verification.Exact:
                    AddRequirement(candidate, resolution, register, seen);
                    break;

                case Verification.Ambiguous:
                    AddDecision(candidate, resolution, decisions, queued);
                    break;

                case Verification.Failed:
                default:
                    rejected.Add(new RejectedQuote(
                        candidate.Quote,
                        candidate.Modality,
                        candidate.Testability,
                        TextNormalizer.Normalize(candidate.Quote).Length == 0
                            ? RejectedQuote.EmptyQuote
                            : RejectedQuote.NotFound));
                    break;
            }
        }

        return new VerificationOutcome(register, rejected, decisions);
    }

    private void AddRequirement(
        CandidateRequirement candidate,
        QuoteResolution resolution,
        List<Requirement> register,
        Dictionary<string, string> seen)
    {
        var id = IdFor(resolution.NormalizedQuote);

        if (seen.TryGetValue(id, out var existing))
        {
            if (string.Equals(existing, resolution.NormalizedQuote, StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Requirement id '{id}' would be shared by two different quotes. "
                + $"First: '{Shorten(existing)}'. Second: '{Shorten(resolution.NormalizedQuote)}'.");
        }

        seen[id] = resolution.NormalizedQuote;

        register.Add(new Requirement(
            id,
            _documentId,
            _sections.SectionFor(resolution.Span!.Value),
            candidate.Modality,
            candidate.Quote,
            resolution.Span.Value,
            candidate.Testability,
            candidate.TestabilityNote));
    }

    private void AddDecision(
        CandidateRequirement candidate,
        QuoteResolution resolution,
        List<DecisionQueueItem> decisions,
        HashSet<string> queued)
    {
        var id = $"DQ-{_documentId}-{ShortHash(resolution.NormalizedQuote)}";

        if (!queued.Add(id))
        {
            return;
        }

        decisions.Add(new DecisionQueueItem(
            id,
            candidate.Quote,
            DecisionQueueItem.NoSingleSection,
            "This quote occurs more than once in the document, so no single span can be claimed "
            + "for it. Which occurrence is meant, or should it be dropped?",
            resolution: null));
    }

    public string IdFor(string normalizedQuote) => $"REQ-{_documentId}-{ShortHash(normalizedQuote)}";

    private static string ShortHash(string normalizedQuote) =>
        Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(normalizedQuote)))[..6];

    private static string Shorten(string text) =>
        text.Length <= 60 ? text : string.Concat(text.AsSpan(0, 60), "…");
}

public sealed record VerificationOutcome(
    IReadOnlyList<Requirement> Register,
    IReadOnlyList<RejectedQuote> Rejected,
    IReadOnlyList<DecisionQueueItem> Decisions)
{
    public double VerificationRate
    {
        get
        {
            var total = Register.Count + Rejected.Count + Decisions.Count;

            return total == 0 ? 0 : (double)Register.Count / total;
        }
    }
}
