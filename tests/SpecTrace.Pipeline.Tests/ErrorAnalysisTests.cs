namespace SpecTrace.Pipeline.Tests;

public sealed class ErrorAnalysisTests
{
    private const string VerdictHeading = "## 5. Unmatched claims: verdicts for the student";

    private static readonly string Experiments = Repository.PathTo("experiments");

    [Fact]
    public void TheVerdictTableListsExactlyTheUnmatchedClaimsTheGeneratedQualityReportLists()
    {
        var generated = TableRows(Path.Combine(Experiments, QualityReport.FileFor("rfc6902")), QualityReport.UnmatchedHeading);
        var verdicts = TableRows(Path.Combine(Experiments, QualityReport.ErrorAnalysisFile), VerdictHeading);

        Assert.NotEmpty(generated);
        Assert.Equal(
            generated.Select(cells => string.Join(" | ", cells)),
            verdicts.Select(cells => string.Join(" | ", cells.Take(4))));
    }

    [Fact]
    public void EveryUnmatchedClaimHasAVerdictColumnOfItsOwn()
    {
        var verdicts = TableRows(Path.Combine(Experiments, QualityReport.ErrorAnalysisFile), VerdictHeading);

        Assert.All(verdicts, cells => Assert.Equal(5, cells.Count));
    }

    private static List<List<string>> TableRows(string path, string heading)
    {
        var lines = File.ReadAllLines(path);
        var start = Array.FindIndex(lines, line => line.Trim() == heading);

        Assert.True(start >= 0, $"{Path.GetFileName(path)} has no '{heading}' section.");

        var header = Array.FindIndex(lines, start, line => line.StartsWith('|'));

        return lines
            .Skip(header + 2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(Cells)
            .ToList();
    }

    private static List<string> Cells(string row)
    {
        const string EscapedBar = "\u0000";

        var inner = row.Replace("\\|", EscapedBar, StringComparison.Ordinal).Trim();
        inner = inner[1..^1];

        return inner.Split('|').Select(cell => cell.Trim().Replace(EscapedBar, "\\|", StringComparison.Ordinal)).ToList();
    }
}
