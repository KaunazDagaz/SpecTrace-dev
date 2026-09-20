namespace SpecTrace.Core;

/// <summary>
/// The result of resolving one quote against a document.
/// </summary>
/// <remarks>
/// Constructed only through the three factory methods, so a span cannot be attached
/// to anything but an <see cref="Verification.Exact"/> result. That is the invalid
/// state this type exists to make unconstructible: a span that was guessed rather
/// than located.
/// </remarks>
public sealed record QuoteResolution
{
    private QuoteResolution(Verification verification, string normalizedQuote, TextSpan? span)
    {
        Verification = verification;
        NormalizedQuote = normalizedQuote;
        Span = span;
    }

    public Verification Verification { get; }

    /// <summary>The quote as normalised for lookup — what was actually searched for.</summary>
    public string NormalizedQuote { get; }

    /// <summary>
    /// Non-null if and only if <see cref="Verification"/> is <see cref="Verification.Exact"/>.
    /// </summary>
    public TextSpan? Span { get; }

    public static QuoteResolution Exact(string normalizedQuote, TextSpan span) =>
        new(Verification.Exact, normalizedQuote, span);

    public static QuoteResolution Ambiguous(string normalizedQuote) =>
        new(Verification.Ambiguous, normalizedQuote, null);

    public static QuoteResolution Failed(string normalizedQuote) =>
        new(Verification.Failed, normalizedQuote, null);
}
