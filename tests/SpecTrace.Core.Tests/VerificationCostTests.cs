namespace SpecTrace.Core.Tests;

public sealed class VerificationCostTests
{
    private static readonly NormalizedDocument Document = NormalizedDocument.Create(
        "1. Ops\n"
        + "Operation objects MUST have exactly one \"op\" member.\n"
        + "Additionally, operation objects MUST have exactly one \"path\" member.\n"
        + "2. Errors\n"
        + "The specified index MUST NOT be greater than the number of elements in the array.\n");

    private static readonly GoldRequirement[] Gold =
    [
        Fixtures.Gold(Document, "Operation objects MUST have exactly one \"op\" member", Modality.Must, 1),
        Fixtures.Gold(Document, "operation objects MUST have exactly one \"path\" member.", Modality.Must, 2),
        Fixtures.Gold(Document, "The specified index MUST NOT be greater than the number of elements in the array.", Modality.MustNot, 3),
    ];

    [Fact]
    public void ARejectedQuoteCloseToAGoldRequirementNoLocatedClaimMatchedCountsAsReachedButLost()
    {
        var cost = Analyse(
            located: ["The specified index MUST NOT be greater than the number of elements in the array."],
            unlocated: ["Operation objects MUST have exactly one 'op' member."]);

        var reach = Assert.Single(cost.Unlocated);
        Assert.True(reach.AtOrAboveThreshold);
        Assert.Equal([0], reach.ClosestGold);
        Assert.Equal(0, reach.CountedGold);
        Assert.Equal([0], cost.GoldReachedButLost);
    }

    [Fact]
    public void ARejectedQuoteCloseToAGoldRequirementALocatedClaimAlreadyMatchedDoesNotCount()
    {
        var cost = Analyse(
            located: ["Operation objects MUST have exactly one \"op\" member."],
            unlocated: ["Operation objects MUST have exactly one 'op' member."]);

        var reach = Assert.Single(cost.Unlocated);
        Assert.Equal([0], reach.ClosestGold);
        Assert.Null(reach.CountedGold);
        Assert.Empty(cost.GoldReachedButLost);
    }

    [Fact]
    public void ARejectedQuoteBelowTheThresholdIsListedWithItsClosestGoldRequirementButNotCounted()
    {
        var cost = Analyse(
            located: [],
            unlocated: ["Operation objects SHOULD carry at most one 'op' member, or else they are refused."]);

        var reach = Assert.Single(cost.Unlocated);
        Assert.False(reach.AtOrAboveThreshold);
        Assert.Equal([0], reach.ClosestGold);
        Assert.Null(reach.CountedGold);
        Assert.Empty(cost.GoldReachedButLost);
    }

    [Fact]
    public void TwoRejectedQuotesCloseToTheSameGoldRequirementCountItOnce()
    {
        var cost = Analyse(
            located: [],
            unlocated:
            [
                "Additionally, operation objects MUST have exactly one 'path' member.",
                "Additionally operation objects MUST have exactly one \"path\" member.",
            ]);

        Assert.Equal([1], cost.GoldReachedButLost);
        Assert.All(cost.Unlocated, reach => Assert.Equal([1], reach.ClosestGold));
        Assert.Single(cost.Unlocated, reach => reach.CountedGold == 1);
        Assert.Equal(2, cost.AtOrAboveThreshold);
    }

    [Fact]
    public void AnAlteredQuoteOfASentenceThatOccursTwiceNamesTheClosestGoldRequirementAtEachPlaceAndCountsOne()
    {
        var document = NormalizedDocument.Create(
            "4.4. move\nThe \"from\" location MUST exist for the operation to be successful.\n"
            + "4.5. copy\nThe \"from\" location MUST exist for the operation to be successful.\n");
        var occurrences = document.Occurrences("The \"from\" location MUST exist for the operation to be successful.");
        GoldRequirement[] gold =
        [
            Fixtures.Gold(occurrences[0], Modality.Must, 1),
            Fixtures.Gold(occurrences[1], Modality.Must, 2),
        ];
        Prediction[] predictions = [new([], modality: null)];

        var cost = VerificationCost.Analyse(
            gold,
            SpanMatching.Match(predictions, gold, 50),
            [new UnlocatedQuote(0, ApproximateLocator.Locate(document, "The 'from' location MUST exist for the operation to be successful."))],
            90);

        var reach = Assert.Single(cost.Unlocated);
        Assert.Equal([0, 1], reach.ClosestGold);
        Assert.Equal(0, reach.CountedGold);
        Assert.Equal([0], cost.GoldReachedButLost);
    }

    private static VerificationCost Analyse(string[] located, string[] unlocated)
    {
        var predictions = located
            .Select(quote => new Prediction([Document.Resolve(quote).Span!.Value], Modality.Must))
            .Concat(unlocated.Select(_ => new Prediction([], Modality.Must)))
            .ToList();

        var match = SpanMatching.Match(predictions, Gold, 50);

        return VerificationCost.Analyse(
            Gold,
            match,
            [.. unlocated.Select((quote, index) => new UnlocatedQuote(located.Length + index, ApproximateLocator.Locate(Document, quote)))],
            90);
    }
}
