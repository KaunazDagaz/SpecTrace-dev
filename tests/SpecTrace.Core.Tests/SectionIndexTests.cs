namespace SpecTrace.Core.Tests;

/// <summary>
/// REQ-ING-02, verified against the committed corpus.
/// </summary>
public sealed class SectionIndexTests
{
    /// <summary>Every header in RFC 6902, in document order.</summary>
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
        // Lines 65-98 are header-shaped and indented by three or five columns. Anchoring
        // at column 0 is the whole reason they are not headers; without it the index
        // would hold every section twice, with the phantom copy first.
        const int TableOfContentsStart = 2170;
        const int IntroductionStart = 4836;

        Assert.DoesNotContain(
            Corpus.Index.Headers,
            header => header.RawOffset >= TableOfContentsStart && header.RawOffset < IntroductionStart);
    }

    [Fact]
    public void RunningPageHeadersAndFootersDoNotRegisterAsSections()
    {
        // "RFC 6902  JSON Patch  April 2013" and "Bryan & Nottingham ... [Page n]" sit at
        // column 0 on each of the 18 page breaks. Requiring the dot after the number or
        // letter is what keeps them out.
        Assert.DoesNotContain(Corpus.Index.Headers, header => header.Title.Contains("[Page", StringComparison.Ordinal));
        Assert.DoesNotContain(Corpus.Index.Headers, header => header.Title.Contains("JSON Patch  ", StringComparison.Ordinal));
    }

    [Fact]
    public void UnnumberedFrontMatterHeadingsAreNotIndexedAsSections()
    {
        // "Abstract", "Status of This Memo", "Copyright Notice" and "Table of Contents"
        // are at column 0 but carry no section number, and REQ-ING-02 resolves a span to
        // the nearest preceding section *number*.
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
        // The pattern suggested in implementation plan §5.5 matches numbered headers only.
        // Under it every offset from 20215 to the end of the file — 389 of 1011 lines —
        // would report "9.2", the last numbered section. This is that regression.
        Assert.Equal("A.16", Corpus.Index.SectionFor(Corpus.Raw.Length - 1));
        Assert.DoesNotContain(
            Corpus.Index.Headers.Where(header => header.RawOffset > 20000),
            header => header.Number == "9.2");
    }

    /// <summary>
    /// The ten sampled spans of REQ-ING-02's acceptance criterion.
    /// </summary>
    /// <remarks>
    /// Each span is located by quoting the document rather than by a hard-coded offset,
    /// so every row can be confirmed by eye: find the quote in <c>corpus/rfc6902.txt</c>,
    /// then read upwards to the nearest line that starts in column 0 with a section
    /// number. The manual steps are written out in the pull request.
    /// </remarks>
    [Theory]
    // 1. Abstract, line 18 — before any numbered header.
    [InlineData(
        "JSON Patch defines a JSON document structure for expressing a sequence of operations",
        "(front matter)")]
    // 2. Section 1, line 121.
    [InlineData(
        "JavaScript Object Notation (JSON) [RFC4627] is a common format for the exchange and storage of structured data.",
        "1")]
    // 3. Section 3, line 144.
    [InlineData(
        "A JSON Patch document is a JSON [RFC4627] document that represents an array of objects.",
        "3")]
    // 4. Section 4.1, line 233 — after a page break, so the raw span is on the far side of one.
    [InlineData(
        "The operation object MUST contain a \"value\" member whose content specifies the value to be added.",
        "4.1")]
    // 5. Section 4.6, line 369 — nearly the same sentence as row 4, different subsection.
    [InlineData(
        "The operation object MUST contain a \"value\" member that conveys the value to be compared",
        "4.6")]
    // 6. Section 5, line 419.
    [InlineData("evaluation of the JSON Patch document SHOULD terminate", "5")]
    // 7. Section 9.2, line 555 — the last numbered section.
    [InlineData("Robust Defenses for Cross-Site Request Forgery", "9.2")]
    // 8. Appendix A, line 623 — the quote spans the header into A.1, so its start is the header.
    [InlineData("Appendix A. Examples A.1. Adding an Object Member", "A")]
    // 9. Appendix A.1, line 637 — inside an indented example block.
    [InlineData("{ \"op\": \"add\", \"path\": \"/baz\", \"value\": \"qux\" }", "A.1")]
    // 10. Appendix A.16, line 976 — the last subsection of the document.
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
        // The carriage return must not end up in the title.
        Assert.Equal("Introduction", index.Headers[0].Title);
    }
}
