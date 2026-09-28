namespace SpecTrace.Core.Tests;

public sealed class SourceContextTests
{
    private const string Document =
        "1.  Introduction\n"
        + "\n"
        + "   The first paragraph has two lines,\n"
        + "   and the quote MUST sit in its second line.\n"
        + "   A third line closes it.\n"
        + "\n"
        + "   Another paragraph.\n";

    [Fact]
    public void TheContextIsTheParagraphHoldingTheQuoteAndStopsAtBlankLines()
    {
        var context = Around("the quote MUST sit");

        Assert.Equal("   The first paragraph has two lines,\n   and ", context.Before);
        Assert.Equal("the quote MUST sit", context.Quote);
        Assert.Equal(" in its second line.\n   A third line closes it.", context.After);
    }

    [Fact]
    public void BeforeQuoteAndAfterJoinToTheRawTextWithoutLosingACharacter()
    {
        var start = Document.IndexOf("two lines,", StringComparison.Ordinal);
        var end = Document.IndexOf("MUST", StringComparison.Ordinal) + "MUST".Length;

        var context = SourceContext.Around(Document, new TextSpan(start, end));

        var paragraphStart = Document.IndexOf("   The first", StringComparison.Ordinal);
        var paragraphEnd = Document.IndexOf("\n\n   Another", StringComparison.Ordinal);

        Assert.Equal(Document[start..end], context.Quote);
        Assert.Contains("\n", context.Quote, StringComparison.Ordinal);
        Assert.Equal(Document[paragraphStart..paragraphEnd], context.Before + context.Quote + context.After);
    }

    [Fact]
    public void TheContextStopsAtWholeLinesOnceItReachesTheLimit()
    {
        var lines = Enumerable.Range(0, 40).Select(index => $"   line {index:D2} of one long paragraph").ToArray();
        var raw = string.Join("\n", lines) + "\n";
        var start = raw.IndexOf("line 20", StringComparison.Ordinal);

        var context = SourceContext.Around(raw, new TextSpan(start, start + "line 20".Length), reach: 100);

        Assert.StartsWith("   line 1", context.Before, StringComparison.Ordinal);
        Assert.DoesNotContain("line 00", context.Before, StringComparison.Ordinal);
        Assert.True(context.Before.Length <= 100 + lines[0].Length + 1);
        Assert.True(context.After.Length <= 100 + lines[0].Length + 1);
        Assert.DoesNotContain("line 39", context.After, StringComparison.Ordinal);
    }

    [Fact]
    public void AQuoteAtTheVeryStartOrEndOfTheDocumentHasAnEmptySideAndNoError()
    {
        const string Raw = "The whole document MUST fit.";

        var context = SourceContext.Around(Raw, new TextSpan(0, Raw.Length));

        Assert.Equal(string.Empty, context.Before);
        Assert.Equal(Raw, context.Quote);
        Assert.Equal(string.Empty, context.After);
    }

    [Fact]
    public void ASpanOutsideTheDocumentIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceContext.Around(Document, new TextSpan(10, Document.Length + 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceContext.Around(Document, new TextSpan(5, 5)));
    }

    private static SourceContext Around(string quote)
    {
        var start = Document.IndexOf(quote, StringComparison.Ordinal);

        return SourceContext.Around(Document, new TextSpan(start, start + quote.Length));
    }
}
