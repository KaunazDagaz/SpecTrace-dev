namespace SpecTrace.Core;

public sealed record ClaimTally
{
    private ClaimTally(int foundOnce, int foundMoreThanOnce, int notFound, int withoutQuote)
    {
        FoundOnce = foundOnce;
        FoundMoreThanOnce = foundMoreThanOnce;
        NotFound = notFound;
        WithoutQuote = withoutQuote;
    }

    public int Claims => FoundOnce + FoundMoreThanOnce + NotFound + WithoutQuote;

    public int FoundOnce { get; }

    public int FoundMoreThanOnce { get; }

    public int NotFound { get; }

    public int WithoutQuote { get; }

    public int QuotesFound => FoundOnce + FoundMoreThanOnce;

    public int NotLocated => NotFound + WithoutQuote;

    public double? NotLocatedShare => Share(NotLocated);

    public double? VerificationRate => Share(FoundOnce);

    public double? FoundMoreThanOnceShare => Share(FoundMoreThanOnce);

    public static ClaimTally Of(IEnumerable<ClaimOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        int foundOnce = 0, foundMoreThanOnce = 0, notFound = 0, withoutQuote = 0;

        foreach (var outcome in outcomes)
        {
            switch (outcome)
            {
                case ClaimOutcome.FoundOnce:
                    foundOnce++;
                    break;
                case ClaimOutcome.FoundMoreThanOnce:
                    foundMoreThanOnce++;
                    break;
                case ClaimOutcome.NotFound:
                    notFound++;
                    break;
                case ClaimOutcome.WithoutQuote:
                    withoutQuote++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(outcomes), outcome, null);
            }
        }

        return new ClaimTally(foundOnce, foundMoreThanOnce, notFound, withoutQuote);
    }

    public static ClaimOutcome OutcomeOf(QuoteResolution? resolution)
    {
        if (resolution is null || resolution.NormalizedQuote.Length == 0)
        {
            return ClaimOutcome.WithoutQuote;
        }

        return resolution.Verification switch
        {
            Verification.Exact => ClaimOutcome.FoundOnce,
            Verification.Ambiguous => ClaimOutcome.FoundMoreThanOnce,
            Verification.Failed => ClaimOutcome.NotFound,
            _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution.Verification, null),
        };
    }

    private double? Share(int count) => Claims == 0 ? null : (double)count / Claims;
}
