namespace SpecTrace.Core;

public sealed record Prediction
{
    public Prediction(IReadOnlyList<TextSpan> spans, Modality? modality)
    {
        ArgumentNullException.ThrowIfNull(spans);

        if (spans.Any(span => span.Length <= 0))
        {
            throw new ArgumentException("Every span of a prediction covers at least one character.", nameof(spans));
        }

        Spans = [.. spans];
        Modality = modality;
    }

    public IReadOnlyList<TextSpan> Spans { get; }

    public Modality? Modality { get; }

    public bool Located => Spans.Count > 0;

    public bool FoundMoreThanOnce => Spans.Count > 1;
}
