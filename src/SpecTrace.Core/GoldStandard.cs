namespace SpecTrace.Core;

public sealed record GoldStandard
{
    public GoldStandard(
        string documentId,
        string rulesCommit,
        string annotator,
        string annotatedAt,
        IReadOnlyList<GoldRequirement> requirements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesCommit);
        ArgumentNullException.ThrowIfNull(annotator);
        ArgumentNullException.ThrowIfNull(annotatedAt);
        ArgumentNullException.ThrowIfNull(requirements);

        DocumentId = documentId;
        RulesCommit = rulesCommit;
        Annotator = annotator;
        AnnotatedAt = annotatedAt;
        Requirements = [.. requirements];
    }

    public string DocumentId { get; }

    public string RulesCommit { get; }

    public string Annotator { get; }

    public string AnnotatedAt { get; }

    public IReadOnlyList<GoldRequirement> Requirements { get; }
}

public sealed record GoldRequirement
{
    public GoldRequirement(
        int candidate,
        int obligation,
        string quote,
        Modality modality,
        Testability testability,
        TextSpan span,
        string section)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(candidate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(obligation, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(quote);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        if (span.Length <= 0)
        {
            throw new ArgumentException(
                $"A gold requirement's span must cover at least one character; got {span}.",
                nameof(span));
        }

        Candidate = candidate;
        Obligation = obligation;
        Quote = quote;
        Modality = modality;
        Testability = testability;
        Span = span;
        Section = section;
    }

    public int Candidate { get; }

    public int Obligation { get; }

    public string Quote { get; }

    public Modality Modality { get; }

    public Testability Testability { get; }

    public TextSpan Span { get; }

    public string Section { get; }
}
