namespace SpecTrace.Core.Tests;

/// <summary>
/// REQ-ING-01, exercised against the committed corpus rather than against invented text.
/// </summary>
public sealed class OffsetMapCorpusTests
{
    /// <summary>Section 4.1, lines 233-234 — one sentence, two raw lines.</summary>
    private const string QuoteAcrossOneLineBreak =
        "The operation object MUST contain a \"value\" member whose content specifies the value to be added.";

    /// <summary>Section 5, lines 419-422 — one sentence, four raw lines.</summary>
    private const string QuoteAcrossThreeLineBreaks =
        "If a normative requirement is violated by a JSON Patch document, or if an operation is not "
        + "successful, evaluation of the JSON Patch document SHOULD terminate and application of the "
        + "entire patch document SHALL NOT be deemed successful.";

    /// <summary>Appendix A.1, line 637 — inside an example block, indented five columns.</summary>
    private const string IndentedQuote = "{ \"op\": \"add\", \"path\": \"/baz\", \"value\": \"qux\" }";

    /// <summary>Line 7 — the first non-blank line of the file.</summary>
    private const string QuoteAtDocumentStart = "Internet Engineering Task Force (IETF) P. Bryan, Ed.";

    /// <summary>Line 1010 — the last non-blank line, a page footer.</summary>
    private const string QuoteAtDocumentEnd = "Bryan & Nottingham Standards Track [Page 18]";

    /// <summary>Occurs 14 times across Appendix A.</summary>
    private const string QuoteThatOccursMoreThanOnce = "A JSON Patch document:";

    [Fact]
    public void TheCommittedCorpusStillHasTheBytesEveryOffsetInTheseTestsAssumes()
    {
        // A canary. If .gitattributes stops holding the corpus at LF, git rewrites the
        // file on checkout and every offset below shifts by one per preceding line.
        // Without this test that failure looks like forty broken offset assertions
        // instead of one line-ending problem.
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

        // The span starts at the "{", not at the five spaces in front of it.
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

        // The file ends with a form feed and a newline after this line, so the last
        // span is not the end of the file.
        Assert.True(span.End < Corpus.Raw.Length);
        Assert.Equal(string.Empty, TextNormalizer.Normalize(Corpus.Raw[span.End..]));
    }

    [Fact]
    public void AnEmptyQuoteIsRejectedRatherThanMatchedAtTheStartOfTheDocument()
    {
        // IndexOf("") returns 0, which would hand back a confident zero-length span
        // at offset 0. Fail closed instead.
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
        // The real sentence says "MUST contain"; this asks for "MUST include".
        var resolution = Corpus.Document.Resolve(
            "The operation object MUST include a \"value\" member whose content specifies the value to be added.");

        Assert.Equal(Verification.Failed, resolution.Verification);
        Assert.Null(resolution.Span);
    }

    [Fact]
    public void TabsCollapseLikeAnyOtherWhitespaceRunAndLeaveTheNormalFormUnchanged()
    {
        // RFC 6902 contains no tab character, so this substitutes tabs for the three-column
        // indent of a real file rather than inventing a tabbed document.
        var tabbed = NormalizedDocument.Create(Corpus.Raw.Replace("\n   ", "\n\t", StringComparison.Ordinal));

        Assert.Equal(Corpus.Document.Normal, tabbed.Normal);
        Assert.Equal(Verification.Exact, tabbed.Resolve(QuoteAcrossThreeLineBreaks).Verification);
    }

    [Fact]
    public void TheSameQuoteResolvesWhetherTheDocumentUsesLfOrCrlf()
    {
        // RFC 6902 ships LF-only, so the CRLF form is derived from the real file.
        var crlf = NormalizedDocument.Create(Corpus.Raw.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(Corpus.Document.Normal, crlf.Normal);

        var lfSpan = AssertExact(QuoteAcrossThreeLineBreaks);
        var crlfResolution = crlf.Resolve(QuoteAcrossThreeLineBreaks);
        Assert.Equal(Verification.Exact, crlfResolution.Verification);
        var crlfSpan = crlfResolution.Span!.Value;

        // The offsets genuinely differ — three extra carriage returns inside the quote,
        // and one per line before it — so the map is doing the work, not luck.
        Assert.NotEqual(lfSpan, crlfSpan);
        Assert.Equal(lfSpan.Length + 3, crlfSpan.Length);
        Assert.Equal(
            TextNormalizer.Normalize(Corpus.Raw[lfSpan.Start..lfSpan.End]),
            TextNormalizer.Normalize(crlf.Raw[crlfSpan.Start..crlfSpan.End]));
    }

    [Fact]
    public void EveryNormalisedSubstringOfTheCorpusMapsBackToRawTextThatNormalisesToIt()
    {
        // REQ-ING-01's acceptance criterion, sampled across the whole document.
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

        // Roughly one normalised character in six is a space, so a little under 70% of
        // random substrings are already trimmed. Sampling until a thousand of them have
        // been checked keeps the coverage fixed instead of leaving it to that ratio.
        for (var attempt = 0; attempt < 10_000 && checkedSamples < 1000; attempt++)
        {
            var start = random.Next(normal.Length);
            var length = random.Next(1, Math.Min(400, normal.Length - start) + 1);

            // A quote always arrives normalised, so it never starts or ends on a space.
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
        // REQ-ING-01 reads "for any substring of the normalized text". Taken literally
        // that cannot hold for a substring with a leading or trailing space, because
        // normalising the raw slice trims it. It holds for every trimmed substring,
        // which is every quote the pipeline ever looks up.
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
