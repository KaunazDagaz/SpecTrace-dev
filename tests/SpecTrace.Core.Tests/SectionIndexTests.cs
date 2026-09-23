namespace SpecTrace.Core.Tests;

public sealed class SectionIndexTests
{
    private static readonly string[] ExpectedSectionNumbers =
    [
        "1", "2", "3", "4", "4.1", "4.2", "4.3", "4.4", "4.5", "4.6",
        "5", "6", "7", "8", "9", "9.1", "9.2",
        "A",
        "A.1", "A.2", "A.3", "A.4", "A.5", "A.6", "A.7", "A.8",
        "A.9", "A.10", "A.11", "A.12", "A.13", "A.14", "A.15", "A.16",
    ];

    [Fact]
    public void TheIndexFindsEveryHeaderInTheDocumentAndNothingElse()
    {
        Assert.Equal(ExpectedSectionNumbers, Corpus.Index.Headers.Select(header => header.Number));
    }

    [Fact]
    public void TheFirstHeaderIsTheIntroductionAtItsKnownOffset()
    {
        var first = Corpus.Index.Headers[0];

        Assert.Equal(4836, first.RawOffset);
        Assert.Equal("1", first.Number);
        Assert.Equal("Introduction", first.Title);
    }

    [Fact]
    public void TheAppendixTitleLineIsIndexedAsItsOwnSection()
    {
        var appendix = Corpus.Index.Headers.Single(header => header.Number == "A");

        Assert.Equal(20215, appendix.RawOffset);
        Assert.Equal("Examples", appendix.Title);
    }

    [Fact]
    public void TheTableOfContentsDoesNotRegisterAsThirtyFourExtraSections()
    {
        const int TableOfContentsStart = 2170;
        const int IntroductionStart = 4836;

        Assert.DoesNotContain(
            Corpus.Index.Headers,
            header => header.RawOffset >= TableOfContentsStart && header.RawOffset < IntroductionStart);
    }

    [Fact]
    public void RunningPageHeadersAndFootersDoNotRegisterAsSections()
    {
        Assert.DoesNotContain(Corpus.Index.Headers, header => header.Title.Contains("[Page", StringComparison.Ordinal));
        Assert.DoesNotContain(Corpus.Index.Headers, header => header.Title.Contains("JSON Patch  ", StringComparison.Ordinal));
    }

    [Fact]
    public void UnnumberedFrontMatterHeadingsAreNotIndexedAsSections()
    {
        Assert.All(Corpus.Index.Headers, header => Assert.True(header.RawOffset >= 4836));
    }

    [Fact]
    public void ASpanBeforeTheFirstHeaderReportsFrontMatterRatherThanAnEmptyString()
    {
        Assert.Equal(SectionIndex.FrontMatter, Corpus.Index.SectionFor(0));
        Assert.Equal(SectionIndex.FrontMatter, Corpus.Index.SectionFor(430));
        Assert.Equal(SectionIndex.FrontMatter, Corpus.Index.SectionFor(4835));
    }

    [Fact]
    public void TheFirstOffsetOfAHeaderBelongsToThatHeaderNotToThePreviousOne()
    {
        Assert.Equal("1", Corpus.Index.SectionFor(4836));
        Assert.Equal(SectionIndex.FrontMatter, Corpus.Index.SectionFor(4835));
    }

    [Fact]
    public void SpansInTheAppendixAreNotLabelledWithTheLastNumberedSection()
    {
        Assert.Equal("A.16", Corpus.Index.SectionFor(Corpus.Raw.Length - 1));
        Assert.DoesNotContain(
            Corpus.Index.Headers.Where(header => header.RawOffset > 20000),
            header => header.Number == "9.2");
    }

    [Theory]
    [InlineData(
        "JSON Patch defines a JSON document structure for expressing a sequence of operations",
        "(front matter)")]
    [InlineData(
        "JavaScript Object Notation (JSON) [RFC4627] is a common format for the exchange and storage of structured data.",
        "1")]
    [InlineData(
        "A JSON Patch document is a JSON [RFC4627] document that represents an array of objects.",
        "3")]
    [InlineData(
        "The operation object MUST contain a \"value\" member whose content specifies the value to be added.",
        "4.1")]
    [InlineData(
        "The operation object MUST contain a \"value\" member that conveys the value to be compared",
        "4.6")]
    [InlineData("evaluation of the JSON Patch document SHOULD terminate", "5")]
    [InlineData("Robust Defenses for Cross-Site Request Forgery", "9.2")]
    [InlineData("Appendix A. Examples A.1. Adding an Object Member", "A")]
    [InlineData("{ \"op\": \"add\", \"path\": \"/baz\", \"value\": \"qux\" }", "A.1")]
    [InlineData("{ \"op\": \"add\", \"path\": \"/foo/-\", \"value\": [\"abc\", \"def\"] }", "A.16")]
    public void ASampledSpanReportsTheSectionThatContainsIt(string quote, string expectedSection)
    {
        var resolution = Corpus.Document.Resolve(quote);

        Assert.Equal(Verification.Exact, resolution.Verification);
        Assert.Equal(expectedSection, Corpus.Index.SectionFor(resolution.Span!.Value));
    }

    [Fact]
    public void AHeaderShapedLineThatIsIndentedIsNotAHeader()
    {
        var index = SectionIndex.Build("1.  Real Header\n   2.  Indented, not a header\n");

        Assert.Equal(["1"], index.Headers.Select(header => header.Number));
    }

    [Fact]
    public void ALineStartingWithACapitalButNoDotIsNotAHeader()
    {
        var index = SectionIndex.Build("RFC 6902                       JSON Patch                     April 2013\n");

        Assert.Empty(index.Headers);
    }

    [Fact]
    public void AHeaderIsStillRecognisedWhenTheDocumentUsesCrlf()
    {
        var index = SectionIndex.Build("1.  Introduction\r\n\r\n4.1.  add\r\n");

        Assert.Equal(["1", "4.1"], index.Headers.Select(header => header.Number));
        Assert.Equal("Introduction", index.Headers[0].Title);
    }
}
