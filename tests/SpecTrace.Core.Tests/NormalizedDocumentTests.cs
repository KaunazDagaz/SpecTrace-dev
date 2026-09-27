namespace SpecTrace.Core.Tests;

public sealed class NormalizedDocumentTests
{
    [Fact]
    public void ARunOfSpacesCollapsesToASingleSpace()
    {
        Assert.Equal("a b", TextNormalizer.Normalize("a      b"));
    }

    [Fact]
    public void AMixedRunOfTabsNewlinesAndSpacesCollapsesToASingleSpace()
    {
        Assert.Equal("a b", TextNormalizer.Normalize("a \t\r\n  \f b"));
    }

    [Fact]
    public void LeadingAndTrailingWhitespaceIsRemoved()
    {
        Assert.Equal("a b", TextNormalizer.Normalize("\r\n   a b \t \n"));
    }

    [Fact]
    public void ACollapsedRunMapsToTheFirstRawCharacterOfThatRun()
    {
        var document = NormalizedDocument.Create("a   b");

        Assert.Equal("a b", document.Normal);
        Assert.Equal(0, document.Map[0]);
        Assert.Equal(1, document.Map[1]);
        Assert.Equal(4, document.Map[2]);
    }

    [Fact]
    public void LeadingWhitespaceIsNotMappedAndDoesNotShiftTheFirstCharacter()
    {
        var document = NormalizedDocument.Create("   abc");

        Assert.Equal("abc", document.Normal);
        Assert.Equal(3, document.Map[0]);
    }

    [Fact]
    public void TheMapHasExactlyOneEntryPerNormalisedCharacter()
    {
        var document = NormalizedDocument.Create("  the\tquick \r\n brown   fox  ");

        Assert.Equal("the quick brown fox", document.Normal);
        Assert.Equal(document.Normal.Length, document.Map.Length);
    }

    [Fact]
    public void ADocumentOfNothingButWhitespaceNormalisesToEmpty()
    {
        var document = NormalizedDocument.Create(" \t\r\n\f ");

        Assert.Equal(string.Empty, document.Normal);
        Assert.Equal(0, document.Map.Length);
    }

    [Fact]
    public void AQuoteFoundTwiceIsReportedAmbiguousRatherThanResolvedToItsFirstMatch()
    {
        var resolution = NormalizedDocument.Create("the cat and the cat").Resolve("the cat");

        Assert.Equal(Verification.Ambiguous, resolution.Verification);
        Assert.Null(resolution.Span);
    }

    [Fact]
    public void OverlappingRepeatsOfAQuoteCountAsRepeats()
    {
        var resolution = NormalizedDocument.Create("aaaa").Resolve("aaa");

        Assert.Equal(Verification.Ambiguous, resolution.Verification);
    }

    [Fact]
    public void ANullQuoteIsRejectedRatherThanTreatedAsEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => NormalizedDocument.Create("abc").Resolve(null!));
    }

    [Fact]
    public void EveryOccurrenceOfAQuoteFoundMoreThanOnceIsListedWithItsRawSpan()
    {
        const string raw = "A: the cat\n   sat.\nB: the cat sat.\nC: the dog sat.";
        var document = NormalizedDocument.Create(raw);

        var occurrences = document.Occurrences("the cat sat.");

        Assert.Equal(2, occurrences.Count);
        Assert.Equal("the cat\n   sat.", raw[occurrences[0].Start..occurrences[0].End]);
        Assert.Equal("the cat sat.", raw[occurrences[1].Start..occurrences[1].End]);
        Assert.Equal(Verification.Ambiguous, document.Resolve("the cat sat.").Verification);
    }

    [Fact]
    public void AQuoteFoundOnceHasOneOccurrenceAtItsResolvedSpanAndAQuoteNotFoundHasNone()
    {
        var document = NormalizedDocument.Create("A: the cat sat.\nB: the dog sat.");

        Assert.Equal([document.Resolve("the dog sat.").Span!.Value], document.Occurrences("the dog sat."));
        Assert.Empty(document.Occurrences("the bird sat."));
        Assert.Empty(document.Occurrences("  \n "));
    }
}
