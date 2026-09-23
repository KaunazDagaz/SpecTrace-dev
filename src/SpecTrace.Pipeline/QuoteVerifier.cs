using System.Security.Cryptography;
using System.Text;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public sealed class QuoteVerifier
{
    private const string AmbiguousQuestion =
        "This quote occurs more than once in the document, so no single span can be claimed "
        + "for it. Which occurrence is meant, or should it be dropped?";

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

        var groups = new List<ClaimGroup>();
        var groupsByQuote = new Dictionary<string, ClaimGroup>(StringComparer.Ordinal);
        var rejected = new List<RejectedQuote>();
        var exactClaims = 0;

        foreach (var candidate in candidates)
        {
            var resolution = _document.Resolve(candidate.Quote);

            if (resolution.Verification == Verification.Failed)
            {
                rejected.Add(new RejectedQuote(
                    candidate.Quote,
                    candidate.Modality,
                    candidate.Testability,
                    resolution.NormalizedQuote.Length == 0 ? RejectedQuote.EmptyQuote : RejectedQuote.NotFound));
                continue;
            }

            if (resolution.Verification == Verification.Exact)
            {
                exactClaims++;
            }

            if (!groupsByQuote.TryGetValue(resolution.NormalizedQuote, out var group))
            {
                group = new ClaimGroup(resolution);
                groupsByQuote.Add(resolution.NormalizedQuote, group);
                groups.Add(group);
            }

            group.Claims.Add(candidate);
        }

        var register = new List<Requirement>();
        var decisions = new List<QueuedDecision>();
        var quotesByHash = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var normalizedQuote = group.Resolution.NormalizedQuote;
            var hash = ClaimHash(normalizedQuote, quotesByHash);
            var first = group.Claims[0];

            if (group.Resolution.Verification == Verification.Ambiguous)
            {
                decisions.Add(new QueuedDecision(
                    new DecisionQueueItem(
                        $"DQ-{_documentId}-{hash}",
                        first.Quote,
                        DecisionQueueItem.NoSingleSection,
                        AmbiguousQuestion,
                        resolution: null),
                    QueuedDecision.QuoteFoundMoreThanOnce,
                    Verification.Ambiguous,
                    group.Claims));
                continue;
            }

            var span = group.Resolution.Span!.Value;
            var section = _sections.SectionFor(span);

            if (group.HasConflictingReadings)
            {
                decisions.Add(new QueuedDecision(
                    new DecisionQueueItem(
                        $"DQ-{_documentId}-{hash}",
                        first.Quote,
                        section,
                        ConflictQuestion(group.Claims),
                        resolution: null),
                    QueuedDecision.ConflictingReadings,
                    Verification.Exact,
                    group.Claims));
                continue;
            }

            register.Add(new Requirement(
                $"REQ-{_documentId}-{hash}",
                _documentId,
                section,
                first.Modality,
                first.Quote,
                span,
                first.Testability,
                first.TestabilityNote));
        }

        return new VerificationOutcome(register, rejected, decisions, candidates.Count, exactClaims);
    }

    public string IdFor(string normalizedQuote) => $"REQ-{_documentId}-{ShortHash(normalizedQuote)}";

    private string ClaimHash(string normalizedQuote, Dictionary<string, string> quotesByHash)
    {
        var hash = ShortHash(normalizedQuote);

        if (quotesByHash.TryGetValue(hash, out var existing)
            && !string.Equals(existing, normalizedQuote, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Requirement id '{IdFor(normalizedQuote)}' would be shared by two different quotes. "
                + $"First: '{Shorten(existing)}'. Second: '{Shorten(normalizedQuote)}'.");
        }

        quotesByHash[hash] = normalizedQuote;
        return hash;
    }

    private static string ConflictQuestion(IReadOnlyList<CandidateRequirement> claims)
    {
        var readings = claims
            .Select(claim => $"{RunArtifacts.Spell(claim.Modality)} ({RunArtifacts.Spell(claim.Testability)})")
            .Distinct();

        return $"The model claimed this quote {claims.Count} times with different readings: "
            + $"{string.Join(", ", readings)}. A sentence can carry more than one obligation. "
            + "Which of these readings apply?";
    }

    private static string ShortHash(string normalizedQuote) =>
        Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(normalizedQuote)))[..6];

    private static string Shorten(string text) =>
        text.Length <= 60 ? text : string.Concat(text.AsSpan(0, 60), "…");

    private sealed class ClaimGroup(QuoteResolution resolution)
    {
        public QuoteResolution Resolution { get; } = resolution;

        public List<CandidateRequirement> Claims { get; } = [];

        public bool HasConflictingReadings =>
            Claims.Select(claim => (claim.Modality, claim.Testability)).Distinct().Count() > 1;
    }
}

public sealed record VerificationOutcome(
    IReadOnlyList<Requirement> Register,
    IReadOnlyList<RejectedQuote> Rejected,
    IReadOnlyList<QueuedDecision> Decisions,
    int ClaimCount,
    int ExactClaimCount)
{
    public int AmbiguousClaimCount => ClaimCount - ExactClaimCount - Rejected.Count;

    public double VerificationRate => ClaimCount == 0 ? 0 : (double)ExactClaimCount / ClaimCount;
}
