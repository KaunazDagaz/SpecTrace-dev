namespace SpecTrace.Core.Tests;

public sealed class QualityScoreTests
{
    [Fact]
    public void PrecisionRecallF1AndModalityAccuracyEqualTheHandCalculatedValues()
    {
        // Gold, four requirements:
        //   g0 = [0, 40) MUST, g1 = [100, 150) MUST_NOT, g2 = [200, 260) SHOULD, g3 = [300, 340) MUST.
        // Predictions, five claims:
        //   p0 = [0, 60) MUST                          overlaps g0 by 40; shorter span 40 -> 100%
        //   p1 = [110, 150) MUST                       overlaps g1 by 40; shorter span 40 -> 100%
        //   p2 = [500, 530) or [210, 240) SHOULD       quote found twice; the second occurrence
        //                                              overlaps g2 by 30; shorter span 30 -> 100%
        //   p3 = [400, 450) MUST                       located, overlaps no gold span
        //   p4 = not located, MUST                     never matches; still counted as a claim
        //
        // Greedy by overlap size: p0-g0 (40), p1-g1 (40), p2-g2 (30). No gold span is wanted twice,
        // so all three are taken. g3 is missed; p3 and p4 are unmatched.
        //
        // Precision = matched / claims      = 3 / 5 = 0.6
        // Recall    = matched / gold        = 3 / 4 = 0.75
        // F1        = 2 * 0.6 * 0.75 / (0.6 + 0.75) = 0.9 / 1.35 = 2/3 = 0.6667
        //
        // Modality over the three matched pairs:
        //   p0 MUST = g0 MUST (correct), p1 MUST vs g1 MUST_NOT (wrong), p2 SHOULD = g2 SHOULD (correct)
        //   accuracy = 2 / 3 = 0.6667
        //
        // Counts: located 4, not located 1, matched through a quote found more than once 1 (p2),
        // located without a gold match 1 (p3), gold missed 1 (g3).
        GoldRequirement[] gold =
        [
            Fixtures.Gold(new TextSpan(0, 40), Modality.Must, 1),
            Fixtures.Gold(new TextSpan(100, 150), Modality.MustNot, 2),
            Fixtures.Gold(new TextSpan(200, 260), Modality.Should, 3),
            Fixtures.Gold(new TextSpan(300, 340), Modality.Must, 4),
        ];
        Prediction[] predictions =
        [
            new([new TextSpan(0, 60)], Modality.Must),
            new([new TextSpan(110, 150)], Modality.Must),
            new([new TextSpan(500, 530), new TextSpan(210, 240)], Modality.Should),
            new([new TextSpan(400, 450)], Modality.Must),
            new([], Modality.Must),
        ];

        var match = SpanMatching.Match(predictions, gold, 50);
        var score = QualityScore.Of(predictions, gold, match);

        Assert.Equal([(0, 0), (1, 1), (2, 2)], match.Matches.Select(pair => (pair.Prediction, pair.Gold)));
        Assert.Equal(0.6, score.Precision!.Value, precision: 12);
        Assert.Equal(0.75, score.Recall!.Value, precision: 12);
        Assert.Equal(2.0 / 3, score.F1!.Value, precision: 12);
        Assert.True(score.ModalityStated);
        Assert.Equal(2, score.ModalityCorrect);
        Assert.Equal(2.0 / 3, score.ModalityAccuracy!.Value, precision: 12);
        Assert.Equal(
            (5, 4, 1, 3, 1, 1, 4, 1),
            (score.Predictions, score.Located, score.NotLocated, score.Matched, score.MatchedThroughQuoteFoundMoreThanOnce,
                score.LocatedWithoutGoldMatch, score.Gold, score.GoldMissed));
    }

    [Fact]
    public void AnArmThatStatesNoModalityHasNoModalityAccuracyRatherThanZero()
    {
        GoldRequirement[] gold = [Fixtures.Gold(new TextSpan(0, 10), Modality.Must)];
        Prediction[] predictions = [new([new TextSpan(0, 10)], modality: null)];

        var score = QualityScore.Of(predictions, gold, SpanMatching.Match(predictions, gold, 50));

        Assert.Equal(1, score.Matched);
        Assert.False(score.ModalityStated);
        Assert.Null(score.ModalityCorrect);
        Assert.Null(score.ModalityAccuracy);
    }

    [Fact]
    public void WithClaimsButNoMatchPrecisionRecallAndF1AreZero()
    {
        GoldRequirement[] gold = [Fixtures.Gold(new TextSpan(0, 10), Modality.Must)];
        Prediction[] predictions = [new([new TextSpan(50, 60)], Modality.Must), new([], Modality.Must)];

        var score = QualityScore.Of(predictions, gold, SpanMatching.Match(predictions, gold, 50));

        Assert.Equal(0, score.Precision);
        Assert.Equal(0, score.Recall);
        Assert.Equal(0, score.F1);
        Assert.Null(score.ModalityAccuracy);
    }

    [Fact]
    public void WithNoClaimsPrecisionAndF1AreUndefinedAndRecallIsZero()
    {
        GoldRequirement[] gold = [Fixtures.Gold(new TextSpan(0, 10), Modality.Must)];

        var score = QualityScore.Of([], gold, SpanMatching.Match([], gold, 50));

        Assert.Null(score.Precision);
        Assert.Equal(0, score.Recall);
        Assert.Null(score.F1);
    }

    [Fact]
    public void AMatchComputedOverOtherListsIsRefused()
    {
        GoldRequirement[] gold = [Fixtures.Gold(new TextSpan(0, 10), Modality.Must)];
        Prediction[] predictions = [new([new TextSpan(0, 10)], Modality.Must)];

        Assert.Throws<ArgumentException>(() => QualityScore.Of(predictions, gold, SpanMatching.Match([], gold, 50)));
    }
}
