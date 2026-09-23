namespace SpecTrace.Core;

public sealed record QuoteResolution
{
    private QuoteResolution(Verification verification, string normalizedQuote, TextSpan? span)
    {
        Verification = verification;
        NormalizedQuote = normalizedQuote;
        Span = span;
    }

    public Verification Verification { get; }

    public string NormalizedQuote { get; }

    public TextSpan? Span { get; }

    public static QuoteResolution Exact(string normalizedQuote, TextSpan span) =>
        new(Verification.Exact, normalizedQuote, span);

    public static QuoteResolution Ambiguous(string normalizedQuote) =>
        new(Verification.Ambiguous, normalizedQuote, null);

    public static QuoteResolution Failed(string normalizedQuote) =>
        new(Verification.Failed, normalizedQuote, null);
}
