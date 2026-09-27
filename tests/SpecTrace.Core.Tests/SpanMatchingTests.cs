namespace SpecTrace.Core.Tests;

public sealed class SpanMatchingTests
{
    [Fact]
    public void APredictionWithExactlyTheGoldSpanMatchesWithTheWholeSpanAsOverlap()
    {
        var result = SpanMatching.Match([Located(10, 30)], [new TextSpan(10, 30)], 50);

        var match = Assert.Single(result.Matches);
        Assert.Equal(0, match.Prediction);
        Assert.Equal(0, match.Gold);
        Assert.Equal(20, match.Overlap);
        Assert.False(match.ThroughQuoteFoundMoreThanOnce);
    }

    [Fact]
    public void FiftyOnePercentOverlapOfTheShorterSpanMatchesAndFortyNinePercentDoesNot()
    {
        TextSpan[] gold = [new(0, 100)];

        Assert.Single(SpanMatching.Match([Located(49, 149)], gold, 50).Matches);
        Assert.Empty(SpanMatching.Match([Located(51, 151)], gold, 50).Matches);
    }

    [Fact]
    public void ExactlyHalfOfTheShorterSpanIsEnoughBecauseTheThresholdIsInclusive()
    {
        Assert.Single(SpanMatching.Match([Located(50, 150)], [new TextSpan(0, 100)], 50).Matches);
    }

    [Fact]
    public void TheOverlapIsMeasuredAgainstTheShorterOfTheTwoSpans()
    {
        TextSpan[] gold = [new(0, 100)];

        Assert.True(SpanMatching.Qualifies(new TextSpan(95, 105), gold[0], 50));
        Assert.False(SpanMatching.Qualifies(new TextSpan(96, 106), gold[0], 50));
        Assert.Single(SpanMatching.Match([Located(95, 105)], gold, 50).Matches);
    }

    [Fact]
    public void SpansThatOnlyTouchDoNotOverlap()
    {
        Assert.Equal(0, SpanMatching.Overlap(new TextSpan(0, 10), new TextSpan(10, 20)));
        Assert.Empty(SpanMatching.Match([Located(0, 10)], [new TextSpan(10, 20)], 1).Matches);
    }

    [Fact]
    public void OneGoldRequirementMatchesOnlyThePredictionWithTheLargerOverlap()
    {
        var result = SpanMatching.Match([Located(0, 60), Located(30, 100)], [new TextSpan(0, 100)], 50);

        var match = Assert.Single(result.Matches);
        Assert.Equal(1, match.Prediction);
        Assert.Equal(70, match.Overlap);
        Assert.Equal([0], result.UnmatchedPredictions);
        Assert.Empty(result.MissedGold);
    }

    [Fact]
    public void APredictionNestedInsideAGoldSpanMatchesAndSoDoesOneThatContainsIt()
    {
        var inside = SpanMatching.Match([Located(50, 80)], [new TextSpan(0, 200)], 50);
        var around = SpanMatching.Match([Located(0, 300)], [new TextSpan(100, 120)], 50);

        Assert.Equal(30, Assert.Single(inside.Matches).Overlap);
        Assert.Equal(20, Assert.Single(around.Matches).Overlap);
    }

    [Fact]
    public void APredictionContainingTwoGoldSpansMatchesOnlyTheOneItOverlapsMost()
    {
        var result = SpanMatching.Match([Located(0, 300)], [new TextSpan(10, 50), new TextSpan(100, 200)], 50);

        var match = Assert.Single(result.Matches);
        Assert.Equal(1, match.Gold);
        Assert.Equal([0], result.MissedGold);
    }

    [Fact]
    public void TwoPredictionsWithTheSameSpanShareTwoGoldSpansByGoldOrderThenPredictionOrder()
    {
        var result = SpanMatching.Match(
            [Located(0, 100), Located(0, 100)],
            [new TextSpan(0, 40), new TextSpan(60, 100)],
            50);

        Assert.Equal([(0, 0), (1, 1)], result.Matches.Select(match => (match.Prediction, match.Gold)));
    }

    [Fact]
    public void AQuoteFoundMoreThanOnceMatchesThroughAnyOccurrenceAndIsMarkedAsSuch()
    {
        var result = SpanMatching.Match(
            [new Prediction([new TextSpan(0, 20), new TextSpan(100, 120)], Modality.Must), Located(300, 320)],
            [new TextSpan(100, 120), new TextSpan(300, 320)],
            50);

        Assert.Equal(2, result.Matches.Count);
        Assert.True(result.ForPrediction(0)!.ThroughQuoteFoundMoreThanOnce);
        Assert.Equal(new TextSpan(100, 120), result.ForPrediction(0)!.Span);
        Assert.False(result.ForPrediction(1)!.ThroughQuoteFoundMoreThanOnce);
    }

    [Fact]
    public void AClaimWhoseQuoteCannotBeLocatedNeverMatches()
    {
        var result = SpanMatching.Match([NotLocated(), Located(0, 10)], [new TextSpan(0, 10)], 1);

        Assert.Equal(1, Assert.Single(result.Matches).Prediction);
        Assert.Equal([0], result.UnmatchedPredictions);
    }

    [Fact]
    public void GreedyTakesTheLargestOverlapFirstEvenWhenAnotherPairingWouldMatchMore()
    {
        var result = SpanMatching.Match(
            [Located(40, 155), Located(50, 100)],
            [new TextSpan(0, 100), new TextSpan(100, 160)],
            50);

        var match = Assert.Single(result.Matches);
        Assert.Equal((0, 0, 60), (match.Prediction, match.Gold, match.Overlap));
        Assert.Equal([1], result.UnmatchedPredictions);
        Assert.Equal([1], result.MissedGold);
    }

    [Fact]
    public void GreedyMatchingAtThirtyFiftyAndSeventyPercentGivesTheHandCalculatedPairs()
    {
        // Gold spans: g0 = [0, 100) (length 100), g1 = [150, 200) (length 50).
        // Predictions: p0 = [40, 140) (length 100), p1 = [60, 170) (length 110), p2 = [180, 190) (length 10).
        //
        // Every pair that overlaps at all, with overlap and the share of the shorter span it covers:
        //   p0-g0: [40, 100)  -> 60 characters; shorter span is 100 (both are 100)  -> 60%
        //   p1-g0: [60, 100)  -> 40 characters; shorter span is g0, 100             -> 40%
        //   p1-g1: [150, 170) -> 20 characters; shorter span is g1, 50              -> 40%
        //   p2-g1: [180, 190) -> 10 characters; shorter span is p2, 10              -> 100%
        //   p0-g1 and p2-g0 do not overlap (p0 ends at 140 before g1 starts at 150).
        //
        // At 50%: pairs that qualify are p0-g0 (60%) and p2-g1 (100%).
        //   Greedy by overlap size: p0-g0 (60) is taken, then p2-g1 (10) is taken.
        //   Matched 2 of 3 predictions, 2 of 2 gold: precision 2/3, recall 2/2 = 1,
        //   F1 = 2 * (2/3) * 1 / (2/3 + 1) = (4/3) / (5/3) = 0.8.
        //
        // At 30%: all four pairs qualify (60%, 40%, 40%, 100%).
        //   Greedy by overlap size: p0-g0 (60) taken; p1-g0 (40) skipped, g0 is taken;
        //   p1-g1 (20) taken; p2-g1 (10) skipped, g1 is taken.
        //   Still 2 matches, so precision 2/3, recall 1, F1 0.8, but g1 now goes to p1, not p2:
        //   size decides, although p2 lies wholly inside g1.
        //
        // At 70%: only p2-g1 (100%) qualifies; p0-g0 is 60%.
        //   Matched 1: precision 1/3, recall 1/2,
        //   F1 = 2 * (1/3) * (1/2) / (1/3 + 1/2) = (1/3) / (5/6) = 0.4.
        Prediction[] predictions = [Located(40, 140), Located(60, 170), Located(180, 190)];
        TextSpan[] gold = [new(0, 100), new(150, 200)];

        var at50 = SpanMatching.Match(predictions, gold, 50);
        var at30 = SpanMatching.Match(predictions, gold, 30);
        var at70 = SpanMatching.Match(predictions, gold, 70);

        Assert.Equal([(0, 0, 60), (2, 1, 10)], at50.Matches.Select(match => (match.Prediction, match.Gold, match.Overlap)));
        Assert.Equal([(0, 0, 60), (1, 1, 20)], at30.Matches.Select(match => (match.Prediction, match.Gold, match.Overlap)));
        Assert.Equal([(2, 1, 10)], at70.Matches.Select(match => (match.Prediction, match.Gold, match.Overlap)));

        var goldRequirements = gold.Select((span, index) => Fixtures.Gold(span, Modality.Must, index + 1)).ToList();

        AssertScore(QualityScore.Of(predictions, goldRequirements, at50), 2.0 / 3, 1, 0.8);
        AssertScore(QualityScore.Of(predictions, goldRequirements, at30), 2.0 / 3, 1, 0.8);
        AssertScore(QualityScore.Of(predictions, goldRequirements, at70), 1.0 / 3, 0.5, 0.4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void AThresholdOutsideOneToOneHundredPercentIsRefused(int percent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpanMatching.Match([Located(0, 10)], [new TextSpan(0, 10)], percent));
    }

    private static void AssertScore(QualityScore score, double precision, double recall, double f1)
    {
        Assert.Equal(precision, score.Precision!.Value, precision: 12);
        Assert.Equal(recall, score.Recall!.Value, precision: 12);
        Assert.Equal(f1, score.F1!.Value, precision: 12);
    }

    private static Prediction Located(int start, int end) => new([new TextSpan(start, end)], Modality.Must);

    private static Prediction NotLocated() => new([], Modality.Must);
}
