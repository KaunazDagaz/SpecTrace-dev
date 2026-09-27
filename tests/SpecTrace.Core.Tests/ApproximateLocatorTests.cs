namespace SpecTrace.Core.Tests;

public sealed class ApproximateLocatorTests
{
    private const string Raw = """
        4.4.  move

           The operation object MUST contain a "from" member.  The "from"
           location MUST exist for the operation to be successful.

        4.5.  copy

           The "from" location MUST exist for the operation to be successful.
           Operation objects MUST have exactly one "op" member.
        """;

    private static readonly NormalizedDocument Document = NormalizedDocument.Create(Raw.ReplaceLineEndings("\n"));

    [Fact]
    public void AVerbatimQuoteIsAtDistanceZeroAtItsResolvedSpan()
    {
        const string quote = "Operation objects MUST have exactly one \"op\" member.";

        var location = ApproximateLocator.Locate(Document, quote);

        Assert.Equal(0, location.Distance);
        Assert.Equal(1.0, location.Similarity);
        Assert.Equal([Document.Resolve(quote).Span!.Value], location.Spans);
    }

    [Fact]
    public void SingleQuotesInPlaceOfDoubleQuotesCostOneEditEach()
    {
        const string quote = "Operation objects MUST have exactly one 'op' member.";

        var location = ApproximateLocator.Locate(Document, quote);

        Assert.Equal(2, location.Distance);
        Assert.Equal(1 - (2.0 / quote.Length), location.Similarity, precision: 12);
        Assert.Equal(
            "Operation objects MUST have exactly one \"op\" member.",
            Text(Assert.Single(location.Spans)));
    }

    [Fact]
    public void AnAlteredSentenceThatOccursTwiceIsPlacedAtBothOccurrences()
    {
        var location = ApproximateLocator.Locate(Document, "The 'from' location MUST exist for the operation to be successful.");

        Assert.Equal(2, location.Distance);
        Assert.Equal(2, location.Spans.Count);
        Assert.All(location.Spans, span => Assert.Equal(
            "The \"from\" location MUST exist for the operation to be successful.",
            TextNormalizer.Normalize(Text(span))));
    }

    [Fact]
    public void AnEllipsisAppendedToAQuoteCostsItsOwnLengthAtMost()
    {
        const string quote = "The operation object MUST contain a \"from\" member. [...]";

        var location = ApproximateLocator.Locate(Document, quote);

        Assert.InRange(location.Distance, 1, 6);
        Assert.StartsWith("The operation object MUST contain a \"from\" member.", Text(Assert.Single(location.Spans)), StringComparison.Ordinal);
    }

    [Fact]
    public void AWhitespaceRunInTheQuoteCountsAsOneSpace()
    {
        var location = ApproximateLocator.Locate(Document, "The operation object\n   MUST contain a \"from\" member.");

        Assert.Equal(0, location.Distance);
    }

    [Fact]
    public void TextTheDocumentDoesNotHoldHasALowSimilarity()
    {
        var location = ApproximateLocator.Locate(Document, "Implementations SHOULD log every rejected request to an audit trail.");

        Assert.True(location.Similarity < 0.6, $"similarity {location.Similarity}");
        Assert.False(location.AtLeast(90));
    }

    [Fact]
    public void TheThresholdIsInclusiveAndComputedWithoutRounding()
    {
        var document = NormalizedDocument.Create("abcdefghijklmnopqrst");

        var two = ApproximateLocator.Locate(document, "abXdefghijklmnopqrYt");
        var three = ApproximateLocator.Locate(document, "abXdefghiZklmnopqrYt");

        Assert.Equal((2, true), (two.Distance, two.AtLeast(90)));
        Assert.Equal((3, false), (three.Distance, three.AtLeast(90)));
        Assert.True(three.AtLeast(85));
    }

    [Fact]
    public void AnEmptyQuoteHasNoLocationAndNoSimilarity()
    {
        var location = ApproximateLocator.Locate(Document, " \n ");

        Assert.Empty(location.Spans);
        Assert.Equal(0, location.Similarity);
        Assert.False(location.AtLeast(1));
    }

    private static string Text(TextSpan span) => Document.Raw[span.Start..span.End];
}
