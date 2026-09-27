using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

public sealed class QualityScoringTests
{
    private const string OpMember = "Operation objects MUST have exactly one \"op\" member";
    private const string OpSentence = "Operation objects MUST have exactly one \"op\" member, whose value indicates the operation to perform.";
    private const string PathSentence = "Additionally, operation objects MUST have exactly one \"path\" member.";
    private const string IndexSentence = "The specified index MUST NOT be greater than the number of elements in the array.";
    private const string TargetExists = "The target location MUST exist for the operation to be successful.";
    private const string Terminate = "evaluation of the JSON Patch document SHOULD terminate";
    private const string NotSuccessful = "application of the entire patch document SHALL NOT be deemed successful";

    private const string SectionFive =
        "If a normative requirement is violated by a JSON Patch document, or if an operation is not successful, "
        + "evaluation of the JSON Patch document SHOULD terminate and application of the entire patch document "
        + "SHALL NOT be deemed successful.";

    private static readonly NormalizedDocument Document = NormalizedDocument.Create(Corpus.Raw);

    [Fact]
    public void AnAnswersClaimsAreScoredOnlyThroughQuotesTheSourceHolds()
    {
        var gold = Gold(
            (Document.Resolve(OpMember).Span!.Value, Modality.Must),
            (Document.Resolve(IndexSentence).Span!.Value, Modality.MustNot),
            (Document.Resolve(PathSentence).Span!.Value, Modality.Must));

        var quality = QualityScoring.ForClaims(
            [
                Claim(1, OpSentence),
                Claim(2, PathSentence.Replace("\"path\"", "'path'", StringComparison.Ordinal)),
                Claim(3, TargetExists),
                Claim(4, null),
            ],
            Corpus.Raw,
            gold);

        var score = quality.Claimed.Primary.Score;

        Assert.Null(quality.Delivered);
        Assert.Equal((4, 2, 1, 1), (score.Predictions, score.Located, score.Matched, score.LocatedWithoutGoldMatch));
        Assert.Equal(0.25, score.Precision);
        Assert.Equal(1.0 / 3, score.Recall!.Value, precision: 12);
        Assert.False(score.ModalityStated);
        Assert.Null(score.ModalityAccuracy);
        Assert.Equal(["4"], quality.Claimed.Predictions[0].Sections);
        Assert.Equal([QualityScoring.NotLocatedSection], quality.Claimed.Predictions[1].Sections);
        Assert.Equal(["4.2", "4.3"], quality.Claimed.Predictions[2].Sections);
        Assert.Equal(1, quality.Claimed.ClaimsWithoutQuote);
    }

    [Fact]
    public void AnAlteredQuoteThatLandsOnAnUnmatchedGoldRequirementIsReachedButLostAndNeverMatched()
    {
        var gold = Gold(
            (Document.Resolve(OpMember).Span!.Value, Modality.Must),
            (Document.Resolve(PathSentence).Span!.Value, Modality.Must));

        var quality = QualityScoring.ForClaims(
            [Claim(1, OpSentence), Claim(2, PathSentence.Replace("\"path\"", "'path'", StringComparison.Ordinal))],
            Corpus.Raw,
            gold);

        var cost = quality.Claimed.Cost;
        var reach = Assert.Single(cost.Unlocated);

        Assert.Equal(1, quality.Claimed.Primary.Score.Matched);
        Assert.Equal(2, reach.Location.Distance);
        Assert.Equal(1, reach.CountedGold);
        Assert.Equal([1], cost.GoldReachedButLost);
        Assert.Equal(["4"], Assert.Single(quality.Claimed.Places).Sections);
        Assert.Equal(PathSentence, Assert.Single(quality.Claimed.Places).Text);
    }

    [Fact]
    public void EveryGoldRequirementFallsInTheThirdOfTheDocumentWhereItsSpanStarts()
    {
        var gold = Gold((Document.Resolve(OpMember).Span!.Value, Modality.Must));

        var quality = QualityScoring.ForClaims([Claim(1, OpSentence)], Corpus.Raw, gold);

        Assert.Equal(1011, quality.DocumentLines);
        Assert.Equal(new GoldPosition(184, 184), Assert.Single(quality.GoldPositions));
        Assert.Equal(
            [(1, 337, 1, 1), (338, 674, 0, 0), (675, 1011, 0, 0)],
            quality.Claimed.Thirds.Select(part => (part.FirstLine, part.LastLine, part.Gold, part.Matched)));
    }

    [Fact]
    public async Task GoldRequirementsTheModelReachedButVerificationKeptOutOfTheRegisterAreHeldBackWithTheirReason()
    {
        var targets = Document.Occurrences(TargetExists);
        var gold = Gold(
            (Document.Resolve(OpMember).Span!.Value, Modality.Must),
            (targets[0], Modality.Must),
            (targets[1], Modality.Must),
            (Document.Resolve(Terminate).Span!.Value, Modality.Should),
            (Document.Resolve(NotSuccessful).Span!.Value, Modality.MustNot));

        var model = RoutingLlmClient.For(
            ModelAnswer.With(
                ("MUST", OpSentence, "testable"),
                ("MUST", TargetExists, "testable"),
                ("MUST", TargetExists, "testable"),
                ("SHOULD", SectionFive, "testable"),
                ("MUST_NOT", SectionFive, "testable")),
            _ => GenerationAnswerJson.Cases(("positive", "a")));

        var run = await PipelineRun.ExecuteAsync(Corpus.Path, model, "gemini-test", CancellationToken.None);
        var quality = QualityScoring.ForPipeline(run, Corpus.Raw, gold);

        Assert.Equal((5, 5, 2), (quality.Claimed.Primary.Score.Predictions, quality.Claimed.Primary.Score.Matched,
            quality.Claimed.Primary.Score.MatchedThroughQuoteFoundMoreThanOnce));
        Assert.Equal((1, 1), (quality.Delivered!.Primary.Score.Predictions, quality.Delivered.Primary.Score.Matched));
        Assert.Equal(
            [
                (1, "2", QueuedDecision.QuoteFoundMoreThanOnce),
                (2, "3", QueuedDecision.QuoteFoundMoreThanOnce),
                (3, "5", QueuedDecision.ConflictingReadings),
                (4, "4", QueuedDecision.ConflictingReadings),
            ],
            quality.HeldBack.Select(held => (held.Gold, held.Claim, held.Reason)));
    }

    [Fact]
    public async Task TwoReadingsOfOneSentenceArePairedWithItsTwoObligationsBySpanAloneSoTheirModalitiesMayCross()
    {
        var gold = Gold(
            (Document.Resolve(Terminate).Span!.Value, Modality.Should),
            (Document.Resolve(NotSuccessful).Span!.Value, Modality.MustNot));

        var model = RoutingLlmClient.For(
            ModelAnswer.With(("SHOULD", SectionFive, "testable"), ("MUST_NOT", SectionFive, "testable")),
            _ => GenerationAnswerJson.Cases(("positive", "a")));

        var run = await PipelineRun.ExecuteAsync(Corpus.Path, model, "gemini-test", CancellationToken.None);
        var score = QualityScoring.ForPipeline(run, Corpus.Raw, gold).Claimed.Primary;

        Assert.Equal([(0, 1), (1, 0)], score.Match.Matches.Select(match => (match.Prediction, match.Gold)));
        Assert.Equal(0, score.Score.ModalityCorrect);
    }

    private static ScoredClaim Claim(int ordinal, string? quote) =>
        new(new ClaimedQuote(ordinal, $"item {ordinal}", ordinal, quote ?? string.Empty, quote), ClaimOutcome.FoundOnce);

    private static GoldReference Gold(params (TextSpan Span, Modality Modality)[] entries) =>
        new(
            new GoldStandard(
                Corpus.DocumentId,
                "0123456789abcdef0123456789abcdef01234567",
                "fixture",
                "2026-09-27",
                [
                    .. entries.Select((entry, index) => new GoldRequirement(
                        index + 1,
                        1,
                        Corpus.Raw[entry.Span.Start..entry.Span.End],
                        entry.Modality,
                        Testability.Testable,
                        entry.Span,
                        SectionIndex.Build(Corpus.Raw).SectionFor(entry.Span))),
                ]),
            "fixture.gold.yaml",
            "0000");
}
