namespace SpecTrace.Core;

public sealed record Requirement
{
    public Requirement(
        string id,
        string documentId,
        string section,
        Modality modality,
        string text,
        TextSpan span,
        Testability testability,
        string? testabilityNote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (span.Length <= 0)
        {
            throw new ArgumentException(
                $"A requirement's span must cover at least one character; got {span}.",
                nameof(span));
        }

        Id = id;
        DocumentId = documentId;
        Section = section;
        Modality = modality;
        Text = text;
        Span = span;
        Testability = testability;
        TestabilityNote = testabilityNote;
    }

    public string Id { get; }

    public string DocumentId { get; }

    public string Section { get; }

    public Modality Modality { get; }

    public string Text { get; }

    public TextSpan Span { get; }

    public Testability Testability { get; }

    public string? TestabilityNote { get; }

    public Verification Verification => Verification.Exact;
}
