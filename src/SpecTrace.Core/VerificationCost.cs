namespace SpecTrace.Core;

public sealed record UnlocatedQuote(int Prediction, ApproximateLocation Location);

public sealed record UnlocatedReach(
    int Prediction,
    ApproximateLocation Location,
    IReadOnlyList<int> ClosestGold,
    bool AtOrAboveThreshold,
    int? CountedGold);

public sealed record VerificationCost
{
    private VerificationCost(int similarityThresholdPercent, IReadOnlyList<UnlocatedReach> unlocated)
    {
        SimilarityThresholdPercent = similarityThresholdPercent;
        Unlocated = [.. unlocated];
        GoldReachedButLost = [.. unlocated.Where(reach => reach.CountedGold is not null).Select(reach => reach.CountedGold!.Value).Order()];
    }

    public int SimilarityThresholdPercent { get; }

    public IReadOnlyList<UnlocatedReach> Unlocated { get; }

    public int AtOrAboveThreshold => Unlocated.Count(reach => reach.AtOrAboveThreshold);

    public IReadOnlyList<int> GoldReachedButLost { get; }

    public static VerificationCost Analyse(
        IReadOnlyList<GoldRequirement> gold,
        MatchResult located,
        IReadOnlyList<UnlocatedQuote> unlocated,
        int similarityThresholdPercent)
    {
        ArgumentNullException.ThrowIfNull(gold);
        ArgumentNullException.ThrowIfNull(located);
        ArgumentNullException.ThrowIfNull(unlocated);
        ArgumentOutOfRangeException.ThrowIfLessThan(similarityThresholdPercent, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(similarityThresholdPercent, 100);

        var percent = located.MinimumOverlapPercent;
        var open = located.MissedGold;
        var reaching = unlocated.Where(quote => quote.Location.AtLeast(similarityThresholdPercent)).ToList();

        var second = SpanMatching.Match(
            [.. reaching.Select(quote => new Prediction(quote.Location.Spans, modality: null))],
            [.. open.Select(index => gold[index].Span)],
            percent);

        var counted = second.Matches.ToDictionary(
            match => reaching[match.Prediction].Prediction,
            match => open[match.Gold]);

        return new VerificationCost(
            similarityThresholdPercent,
            [.. unlocated.Select(quote => new UnlocatedReach(
                quote.Prediction,
                quote.Location,
                Closest(gold, quote.Location, percent),
                quote.Location.AtLeast(similarityThresholdPercent),
                counted.TryGetValue(quote.Prediction, out var goldIndex) ? goldIndex : null))]);
    }

    private static List<int> Closest(IReadOnlyList<GoldRequirement> gold, ApproximateLocation location, int percent) =>
    [
        .. location.Spans
            .Select(span => Enumerable.Range(0, gold.Count)
                .Where(index => SpanMatching.Qualifies(span, gold[index].Span, percent))
                .OrderByDescending(index => SpanMatching.Overlap(span, gold[index].Span))
                .ThenBy(index => gold[index].Span.Start)
                .Select(index => (int?)index)
                .FirstOrDefault())
            .OfType<int>()
            .Distinct()
            .Order(),
    ];
}
