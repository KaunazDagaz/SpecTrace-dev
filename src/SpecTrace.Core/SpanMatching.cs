namespace SpecTrace.Core;

public sealed record SpanMatch(int Prediction, int Gold, TextSpan Span, int Overlap, bool ThroughQuoteFoundMoreThanOnce);

public sealed record MatchResult
{
    internal MatchResult(int minimumOverlapPercent, int predictionCount, int goldCount, IReadOnlyList<SpanMatch> matches)
    {
        MinimumOverlapPercent = minimumOverlapPercent;
        PredictionCount = predictionCount;
        GoldCount = goldCount;
        Matches = [.. matches.OrderBy(match => match.Prediction)];
        UnmatchedPredictions = [.. Enumerable.Range(0, predictionCount).Where(prediction => ForPrediction(prediction) is null)];
        MissedGold = [.. Enumerable.Range(0, goldCount).Where(gold => ForGold(gold) is null)];
    }

    public int MinimumOverlapPercent { get; }

    public int PredictionCount { get; }

    public int GoldCount { get; }

    public IReadOnlyList<SpanMatch> Matches { get; }

    public IReadOnlyList<int> UnmatchedPredictions { get; }

    public IReadOnlyList<int> MissedGold { get; }

    public SpanMatch? ForPrediction(int prediction) => Matches.FirstOrDefault(match => match.Prediction == prediction);

    public SpanMatch? ForGold(int gold) => Matches.FirstOrDefault(match => match.Gold == gold);
}

public static class SpanMatching
{
    public const int DefaultMinimumOverlapPercent = 50;

    public static int Overlap(TextSpan first, TextSpan second) =>
        Math.Max(0, Math.Min(first.End, second.End) - Math.Max(first.Start, second.Start));

    public static bool Qualifies(TextSpan prediction, TextSpan gold, int minimumOverlapPercent)
    {
        CheckPercent(minimumOverlapPercent);

        var overlap = Overlap(prediction, gold);
        var shorter = Math.Min(prediction.Length, gold.Length);

        return overlap > 0 && overlap * 100L >= (long)minimumOverlapPercent * shorter;
    }

    public static MatchResult Match(
        IReadOnlyList<Prediction> predictions,
        IReadOnlyList<GoldRequirement> gold,
        int minimumOverlapPercent)
    {
        ArgumentNullException.ThrowIfNull(gold);

        return Match(predictions, [.. gold.Select(requirement => requirement.Span)], minimumOverlapPercent);
    }

    public static MatchResult Match(
        IReadOnlyList<Prediction> predictions,
        IReadOnlyList<TextSpan> gold,
        int minimumOverlapPercent)
    {
        ArgumentNullException.ThrowIfNull(predictions);
        ArgumentNullException.ThrowIfNull(gold);
        CheckPercent(minimumOverlapPercent);

        var candidates = new List<SpanMatch>();

        for (var prediction = 0; prediction < predictions.Count; prediction++)
        {
            var spans = predictions[prediction].Spans;

            for (var goldIndex = 0; goldIndex < gold.Count; goldIndex++)
            {
                var best = spans
                    .Where(span => Qualifies(span, gold[goldIndex], minimumOverlapPercent))
                    .Select(span => (Span: span, Overlap: Overlap(span, gold[goldIndex])))
                    .OrderByDescending(pair => pair.Overlap)
                    .ThenBy(pair => pair.Span.Start)
                    .FirstOrDefault();

                if (best.Overlap > 0)
                {
                    candidates.Add(new SpanMatch(prediction, goldIndex, best.Span, best.Overlap, spans.Count > 1));
                }
            }
        }

        var matched = new List<SpanMatch>();
        var takenPredictions = new HashSet<int>();
        var takenGold = new HashSet<int>();

        foreach (var candidate in candidates
            .OrderByDescending(candidate => candidate.Overlap)
            .ThenBy(candidate => gold[candidate.Gold].Start)
            .ThenBy(candidate => candidate.Gold)
            .ThenBy(candidate => candidate.Prediction))
        {
            if (takenPredictions.Contains(candidate.Prediction) || takenGold.Contains(candidate.Gold))
            {
                continue;
            }

            takenPredictions.Add(candidate.Prediction);
            takenGold.Add(candidate.Gold);
            matched.Add(candidate);
        }

        return new MatchResult(minimumOverlapPercent, predictions.Count, gold.Count, matched);
    }

    private static void CheckPercent(int minimumOverlapPercent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumOverlapPercent, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumOverlapPercent, 100);
    }
}
