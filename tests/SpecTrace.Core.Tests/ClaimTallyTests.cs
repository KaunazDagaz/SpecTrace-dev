namespace SpecTrace.Core.Tests;

public sealed class ClaimTallyTests
{
    [Fact]
    public void EachOutcomeIsCountedOnceAndTheClaimsAreTheirSum()
    {
        var tally = ClaimTally.Of(
        [
            ClaimOutcome.FoundOnce, ClaimOutcome.FoundOnce, ClaimOutcome.FoundOnce,
            ClaimOutcome.FoundMoreThanOnce,
            ClaimOutcome.NotFound, ClaimOutcome.NotFound,
            ClaimOutcome.WithoutQuote,
            ClaimOutcome.FoundOnce,
        ]);

        Assert.Equal(8, tally.Claims);
        Assert.Equal(4, tally.FoundOnce);
        Assert.Equal(1, tally.FoundMoreThanOnce);
        Assert.Equal(2, tally.NotFound);
        Assert.Equal(1, tally.WithoutQuote);
        Assert.Equal(5, tally.QuotesFound);
        Assert.Equal(3, tally.NotLocated);
    }

    [Fact]
    public void AClaimWithNoQuoteCountsAsNotLocatedBesideTheQuotesThatWereNotFound()
    {
        var tally = ClaimTally.Of([ClaimOutcome.FoundOnce, ClaimOutcome.NotFound, ClaimOutcome.WithoutQuote, ClaimOutcome.FoundOnce]);

        Assert.Equal(0.5, tally.NotLocatedShare);
        Assert.Equal(0.5, tally.VerificationRate);
        Assert.Equal(0, tally.FoundMoreThanOnceShare);
    }

    [Fact]
    public void NotLocatedFoundOnceAndFoundMoreThanOnceAddUpToTheWhole()
    {
        var tally = ClaimTally.Of(
        [
            ClaimOutcome.FoundOnce, ClaimOutcome.FoundMoreThanOnce, ClaimOutcome.FoundMoreThanOnce,
            ClaimOutcome.NotFound, ClaimOutcome.WithoutQuote, ClaimOutcome.NotFound, ClaimOutcome.FoundOnce,
        ]);

        Assert.Equal(3.0 / 7, tally.NotLocatedShare!.Value, precision: 12);
        Assert.Equal(2.0 / 7, tally.VerificationRate!.Value, precision: 12);
        Assert.Equal(2.0 / 7, tally.FoundMoreThanOnceShare!.Value, precision: 12);
        Assert.Equal(1.0, tally.NotLocatedShare!.Value + tally.VerificationRate!.Value + tally.FoundMoreThanOnceShare!.Value, precision: 12);
    }

    [Fact]
    public void WithNoClaimsEveryShareIsUndefinedRatherThanZero()
    {
        var tally = ClaimTally.Of([]);

        Assert.Equal(0, tally.Claims);
        Assert.Null(tally.NotLocatedShare);
        Assert.Null(tally.VerificationRate);
        Assert.Null(tally.FoundMoreThanOnceShare);
    }

    [Fact]
    public void TheOutcomeOfAQuoteIsReadFromItsResolutionInTheDocument()
    {
        var document = NormalizedDocument.Create("The target MUST exist.\nA pointer MAY be empty. A pointer MAY be empty.");

        Assert.Equal(ClaimOutcome.FoundOnce, ClaimTally.OutcomeOf(document.Resolve("The target MUST\n   exist.")));
        Assert.Equal(ClaimOutcome.FoundMoreThanOnce, ClaimTally.OutcomeOf(document.Resolve("A pointer MAY be empty.")));
        Assert.Equal(ClaimOutcome.NotFound, ClaimTally.OutcomeOf(document.Resolve("The target SHOULD exist.")));
    }

    [Fact]
    public void NoQuoteAndAQuoteThatIsOnlyWhitespaceAreBothClaimsWithoutAQuote()
    {
        var document = NormalizedDocument.Create("The target MUST exist.");

        Assert.Equal(ClaimOutcome.WithoutQuote, ClaimTally.OutcomeOf(null));
        Assert.Equal(ClaimOutcome.WithoutQuote, ClaimTally.OutcomeOf(document.Resolve(" \n\t ")));
    }
}
