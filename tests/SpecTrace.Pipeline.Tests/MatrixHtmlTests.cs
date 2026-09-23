using System.Text.RegularExpressions;
using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

public sealed partial class MatrixHtmlTests
{
    private const string HostileText = "A <script>alert(1)</script> member & \"quoted\" 'text' MUST be ignored.";

    [Fact]
    public void EveryPieceOfDocumentAndModelTextIsHtmlEncoded()
    {
        var html = Render(out _);

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
        Assert.Contains("A &lt;script&gt;alert(1)&lt;/script&gt; member &amp; &quot;quoted&quot; &#39;text&#39;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;bold&lt;/b&gt; title", html, StringComparison.Ordinal);
        Assert.Contains("blocked &lt;because&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTopOfThePageSaysCoverageIsByProposedUnreviewedCasesAndClaimsNoCompleteness()
    {
        var html = Render(out _);

        var notice = html.IndexOf("Coverage here is by proposed, unreviewed test cases.", StringComparison.Ordinal);
        var disclaimer = html.IndexOf(MatrixHtml.NoCompletenessClaim, StringComparison.Ordinal);
        var queue = html.IndexOf("id=\"decision-queue\"", StringComparison.Ordinal);
        var matrix = html.IndexOf("id=\"matrix\"", StringComparison.Ordinal);

        Assert.True(notice > 0 && disclaimer > notice && queue > disclaimer && matrix > queue);
        Assert.Equal(1, Regex.Count(html, "fully covered"));
        Assert.Contains("1 of the 1 test cases have not been reviewed by a person", html, StringComparison.Ordinal);
    }

    [Fact]
    public void GapsAndTheDecisionQueueAreMarkedSoTheyAreEasyToSpot()
    {
        var html = Render(out var matrix);

        var gaps = matrix.Rows.Count(row => row.Status == CoverageStatus.Gap);

        Assert.Equal(1, gaps);
        Assert.Equal(gaps, Regex.Count(html, "<tr class=\"gap\">"));
        Assert.Equal(gaps, Regex.Count(html, "<td class=\"gap\">GAP"));
        Assert.Contains("<strong>Gaps: 1</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Items in the decision queue: 1</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<section id=\"decision-queue\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageCarriesNoTimestampNoScriptAndOnlyLineFeeds()
    {
        var html = Render(out _);

        Assert.False(DateLike().IsMatch(html), "The page contains something that looks like a date or a time.");
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\r', html);
    }

    [Fact]
    public void RenderingTheSameInputsTwiceGivesTheSameText()
    {
        Assert.Equal(Render(out _), Render(out _));
    }

    private static string Render(out TraceabilityMatrix matrix)
    {
        Requirement covered = new(
            "REQ-doc-aaaaaa", "doc", "4.1", Modality.Must, HostileText, new TextSpan(100, 160), Testability.Testable, null);
        Requirement gap = new(
            "REQ-doc-bbbbbb", "doc", "4.3", Modality.MustNot, "The value MUST NOT change.", new TextSpan(300, 330), Testability.Testable, null);

        TestCase testCase = new(
            "TC-aaaaaa-01",
            ["REQ-doc-aaaaaa"],
            "<b>bold</b> title",
            CaseType.Negative,
            "A target & a patch.",
            "An input with \"quotes\".",
            "The result is <unchanged>.",
            ReviewStatus.Proposed);

        Requirement[] register = [gap, covered];
        TestCase[] cases = [testCase];

        matrix = TraceabilityMatrix.Build(register, cases, []);

        return MatrixHtml.Render(
            "doc",
            register,
            cases,
            matrix,
            [QueuedDecision.Blocked(gap, "blocked <because> the quote is short")]);
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}|\d{1,2}:\d{2}")]
    private static partial Regex DateLike();
}
