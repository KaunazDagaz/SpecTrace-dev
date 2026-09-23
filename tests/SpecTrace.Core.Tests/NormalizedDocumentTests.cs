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
}
