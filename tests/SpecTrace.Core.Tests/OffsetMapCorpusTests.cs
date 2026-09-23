namespace SpecTrace.Core.Tests;

public sealed class OffsetMapCorpusTests
{
    private const string QuoteAcrossOneLineBreak =
        "The operation object MUST contain a \"value\" member whose content specifies the value to be added.";

    private const string QuoteAcrossThreeLineBreaks =
        "If a normative requirement is violated by a JSON Patch document, or if an operation is not "
        + "successful, evaluation of the JSON Patch document SHOULD terminate and application of the "
        + "entire patch document SHALL NOT be deemed successful.";

    private const string IndentedQuote = "{ \"op\": \"add\", \"path\": \"/baz\", \"value\": \"qux\" }";

    private const string QuoteAtDocumentStart = "Internet Engineering Task Force (IETF) P. Bryan, Ed.";

    private const string QuoteAtDocumentEnd = "Bryan & Nottingham Standards Track [Page 18]";

    private const string QuoteThatOccursMoreThanOnce = "A JSON Patch document:";

    [Fact]
    public void TheCommittedCorpusStillHasTheBytesEveryOffsetInTheseTestsAssumes()
    {
        Assert.Equal(Corpus.RawLength, Corpus.Raw.Length);
        Assert.DoesNotContain('\r', Corpus.Raw);
    }

    [Fact]
    public void AQuoteWrappedAcrossOneLineIsLocatedInTheRawFile()
    {
        var span = AssertExact(QuoteAcrossOneLineBreak);
        var rawText = Corpus.Raw[span.Start..span.End];

        Assert.Contains('\n', rawText);
        Assert.Equal(QuoteAcrossOneLineBreak, TextNormalizer.Normalize(rawText));
    }

    [Fact]
    public void AQuoteWrappedAcrossFourLinesIsLocatedInTheRawFile()
    {
        var span = AssertExact(QuoteAcrossThreeLineBreaks);
        var rawText = Corpus.Raw[span.Start..span.End];

        Assert.Equal(3, rawText.Count(character => character == '\n'));
        Assert.Equal(QuoteAcrossThreeLineBreaks, TextNormalizer.Normalize(rawText));
    }

    [Fact]
    public void AQuoteInsideAnIndentedBlockIsLocatedWithoutItsIndentation()
    {
        var span = AssertExact(IndentedQuote);

        Assert.Equal('{', Corpus.Raw[span.Start]);
        Assert.Equal(IndentedQuote, TextNormalizer.Normalize(Corpus.Raw[span.Start..span.End]));
    }

    [Fact]
    public void AQuoteAtTheStartOfTheDocumentSkipsTheSixLeadingBlankLines()
    {
        var span = AssertExact(QuoteAtDocumentStart);

        Assert.Equal(Corpus.FirstContentOffset, span.Start);
        Assert.Equal(Corpus.FirstContentOffset, Corpus.Document.Map[0]);
    }

    [Fact]
    public void AQuoteAtTheEndOfTheDocumentStopsBeforeTheTrailingPageBreak()
    {
        var span = AssertExact(QuoteAtDocumentEnd);

        Assert.True(span.End < Corpus.Raw.Length);
        Assert.Equal(string.Empty, TextNormalizer.Normalize(Corpus.Raw[span.End..]));
    }

    [Fact]
    public void AnEmptyQuoteIsRejectedRatherThanMatchedAtTheStartOfTheDocument()
    {
        var resolution = Corpus.Document.Resolve(string.Empty);

        Assert.Equal(Verification.Failed, resolution.Verification);
        Assert.Null(resolution.Span);
    }

    [Fact]
    public void AWhitespaceOnlyQuoteIsRejectedRatherThanMatchedAtTheStartOfTheDocument()
    {
        var resolution = Corpus.Document.Resolve(" \t\r\n ");

        Assert.Equal(Verification.Failed, resolution.Verification);
        Assert.Null(resolution.Span);
    }

    [Fact]
    public void AQuoteThatOccursTwiceInTheDocumentIsAmbiguousAndClaimsNoSpan()
    {
        var resolution = Corpus.Document.Resolve(QuoteThatOccursMoreThanOnce);

        Assert.Equal(Verification.Ambiguous, resolution.Verification);
        Assert.Null(resolution.Span);
    }

    [Fact]
    public void AQuoteThatIsNotInTheDocumentIsRejectedRatherThanApproximated()
    {
        var resolution = Corpus.Document.Resolve(
            "The operation object MUST include a \"value\" member whose content specifies the value to be added.");

        Assert.Equal(Verification.Failed, resolution.Verification);
        Assert.Null(resolution.Span);
    }

    [Fact]
    public void TabsCollapseLikeAnyOtherWhitespaceRunAndLeaveTheNormalFormUnchanged()
    {
        var tabbed = NormalizedDocument.Create(Corpus.Raw.Replace("\n   ", "\n\t", StringComparison.Ordinal));

        Assert.Equal(Corpus.Document.Normal, tabbed.Normal);
        Assert.Equal(Verification.Exact, tabbed.Resolve(QuoteAcrossThreeLineBreaks).Verification);
    }

    [Fact]
    public void TheSameQuoteResolvesWhetherTheDocumentUsesLfOrCrlf()
    {
        var crlf = NormalizedDocument.Create(Corpus.Raw.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(Corpus.Document.Normal, crlf.Normal);

        var lfSpan = AssertExact(QuoteAcrossThreeLineBreaks);
        var crlfResolution = crlf.Resolve(QuoteAcrossThreeLineBreaks);
        Assert.Equal(Verification.Exact, crlfResolution.Verification);
        var crlfSpan = crlfResolution.Span!.Value;

        Assert.NotEqual(lfSpan, crlfSpan);
        Assert.Equal(lfSpan.Length + 3, crlfSpan.Length);
        Assert.Equal(
            TextNormalizer.Normalize(Corpus.Raw[lfSpan.Start..lfSpan.End]),
            TextNormalizer.Normalize(crlf.Raw[crlfSpan.Start..crlfSpan.End]));
    }

    [Fact]
    public void EveryNormalisedSubstringOfTheCorpusMapsBackToRawTextThatNormalisesToIt()
    {
        var random = new Random(Seed: 20260920);
        var normal = Corpus.Document.Normal;

        for (var sample = 0; sample < 1000; sample++)
        {
            var start = random.Next(normal.Length);
            var length = random.Next(1, Math.Min(400, normal.Length - start) + 1);
            var substring = normal.Substring(start, length);
            var span = SpanOf(start, length);

            Assert.Equal(
                TextNormalizer.Normalize(substring),
                TextNormalizer.Normalize(Corpus.Raw[span.Start..span.End]));
        }
    }

    [Fact]
    public void ATrimmedSubstringOfTheCorpusRoundTripsToItselfExactly()
    {
        var random = new Random(Seed: 20260920);
        var normal = Corpus.Document.Normal;
        var checkedSamples = 0;

        for (var attempt = 0; attempt < 10_000 && checkedSamples < 1000; attempt++)
        {
            var start = random.Next(normal.Length);
            var length = random.Next(1, Math.Min(400, normal.Length - start) + 1);

            if (normal[start] == ' ' || normal[start + length - 1] == ' ')
            {
                continue;
            }

            var substring = normal.Substring(start, length);
            var span = SpanOf(start, length);

            Assert.Equal(substring, TextNormalizer.Normalize(Corpus.Raw[span.Start..span.End]));
            checkedSamples++;
        }

        Assert.Equal(1000, checkedSamples);
    }

    [Fact]
    public void ASubstringThatStartsOrEndsOnASpaceRoundTripsWithoutThatSpace()
    {
        var normal = Corpus.Document.Normal;
        var start = normal.IndexOf(QuoteAcrossOneLineBreak, StringComparison.Ordinal);

        Assert.True(start > 0);
        Assert.Equal(' ', normal[start - 1]);
        Assert.Equal(' ', normal[start + QuoteAcrossOneLineBreak.Length]);

        var widened = normal.Substring(start - 1, QuoteAcrossOneLineBreak.Length + 2);
        var span = SpanOf(start - 1, widened.Length);
        var roundTripped = TextNormalizer.Normalize(Corpus.Raw[span.Start..span.End]);

        Assert.NotEqual(widened, roundTripped);
        Assert.Equal(QuoteAcrossOneLineBreak, roundTripped);
    }

    private static TextSpan AssertExact(string quote)
    {
        var resolution = Corpus.Document.Resolve(quote);

        Assert.Equal(Verification.Exact, resolution.Verification);
        Assert.NotNull(resolution.Span);

        return resolution.Span.Value;
    }

    private static TextSpan SpanOf(int normalStart, int length)
    {
        var map = Corpus.Document.Map;
        return new TextSpan(map[normalStart], map[normalStart + length - 1] + 1);
    }
}
