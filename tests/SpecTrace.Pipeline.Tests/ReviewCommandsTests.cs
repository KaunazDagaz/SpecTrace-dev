using SpecTrace.Cli;
using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

public sealed class ReviewCommandsTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExportWritesTheReviewedMatrixAsMarkdownAndCsvWithTheSameRowsAsTheReviewedView()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var log = ReviewWorkspace.LogOf(workspace);

        await ReviewLog.AppendAsync(log, Case("TC-1d542c-01", CaseDecision.Reject), CancellationToken.None);
        await ReviewLog.AppendAsync(log, Case("TC-1d542c-02", CaseDecision.Reject), CancellationToken.None);
        await ReviewLog.AppendAsync(
            log,
            new QueueResolution("rfc6902-3ff2234db6aa", "DQ-rfc6902-5a829e", "REQ-rfc6902-5a829e", QueueDecision.NotTestable, "Ann", At),
            CancellationToken.None);

        var exports = Path.Combine(scratch.Path, "exports");
        var (exitCode, output, error) = await RunAsync(
            "export", "--run", "reference",
            "--runs", workspace.Runs, "--reference", workspace.Reference, "--corpus", workspace.Corpus,
            "--out", exports);

        Assert.True(exitCode == SpecTraceCli.ExitSuccess, error);
        Assert.Contains("3 decisions", output, StringComparison.Ordinal);

        var view = await new RunCatalog(workspace, TimeProvider.System).LoadAsync(Workspace.ReferenceKey, CancellationToken.None);
        var expected = MatrixExport.RowsOf(view)
            .Select(row => $"{row.Requirement.Id},{RunArtifacts.Spell(row.Status)}")
            .ToList();
        var csv = (await File.ReadAllTextAsync(Path.Combine(exports, "reference.matrix.csv")))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)[1..]
            .Select(line => line.Split(','))
            .Select(fields => $"{fields[0]},{fields[3]}")
            .ToList();
        var markdown = await File.ReadAllTextAsync(Path.Combine(exports, "reference.matrix.md"));

        Assert.Equal(expected, csv);
        Assert.Contains("REQ-rfc6902-1d542c,gap", csv);
        Assert.Contains("REQ-rfc6902-5a829e,not_testable", csv);
        Assert.Contains(MatrixHtml.NoCompletenessClaim, markdown, StringComparison.Ordinal);
        Assert.Contains("| REQ-rfc6902-1d542c | 4 | MUST | gap |", markdown, StringComparison.Ordinal);
        Assert.Equal(RunComparison.Snapshot(Repository.PathTo("runs", "reference")), RunComparison.Snapshot(workspace.Reference));
    }

    [Fact]
    public async Task ExportOfARunThatDoesNotExistExportsNothing()
    {
        using var scratch = new ScratchDirectory();
        var exports = Path.Combine(scratch.Path, "exports");

        var (exitCode, _, error) = await RunAsync(
            "export", "--run", "rfc6902-000000000000", "--runs", Path.Combine(scratch.Path, "runs"), "--out", exports);

        Assert.Equal(SpecTraceCli.ExitFailure, exitCode);
        Assert.Contains("Nothing was exported", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(exports));
    }

    [Fact]
    public async Task ScoreReviewsCountsCasesByTheirLatestDecisionAndWritesTheSummaryOnlyWhenAsked()
    {
        using var scratch = new ScratchDirectory();
        var log = Path.Combine(scratch.Path, "walkthrough.jsonl");
        var edit = new CaseEdit("A sharper title", CaseType.Negative, string.Empty, "A patch.", "It is rejected.");

        foreach (var decision in new LoggedDecision[]
        {
            Case("TC-07e6ff-01", CaseDecision.Reject),
            Case("TC-07e6ff-01", CaseDecision.Accept),
            Case("TC-07e6ff-02", CaseDecision.Accept),
            new ReviewRecord("rfc6902-3ff2234db6aa", "TC-07e6ff-03", CaseDecision.Edit, edit, "Ann", At),
            Case("TC-1d542c-01", CaseDecision.Reject),
            new QueueResolution("rfc6902-3ff2234db6aa", "DQ-rfc6902-5a829e", "REQ-rfc6902-5a829e", QueueDecision.Defer, "Ann", At),
        })
        {
            await ReviewLog.AppendAsync(log, decision, CancellationToken.None);
        }

        var (printed, printedOutput, printedError) = await RunAsync("score", "--reviews", log);

        Assert.True(printed == SpecTraceCli.ExitSuccess, printedError);
        Assert.Contains("Test cases decided: 4; accepted as proposed 2 (50.0 %), edited 1 (25.0 %), rejected 1 (25.0 %)", printedOutput, StringComparison.Ordinal);
        Assert.Contains("Superseded decisions, followed by a later line on the same case or item: 1", printedOutput, StringComparison.Ordinal);
        Assert.Contains("deferred 1", printedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("written to", printedOutput, StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(scratch.Path));

        var outDirectory = Path.Combine(scratch.Path, "experiments");
        var (written, writtenOutput, writtenError) = await RunAsync("score", "--reviews", log, "--out", outDirectory);
        var summary = await File.ReadAllTextAsync(Path.Combine(outDirectory, "rfc6902-3ff2234db6aa.review-outcomes.md"));

        Assert.True(written == SpecTraceCli.ExitSuccess, writtenError);
        Assert.Contains("written to", writtenOutput, StringComparison.Ordinal);
        Assert.Contains("# Review outcomes: rfc6902-3ff2234db6aa", summary, StringComparison.Ordinal);
        Assert.Contains("`walkthrough.jsonl`", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(scratch.Path, summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScoreReviewsRefusesALogWithAMalformedLine()
    {
        using var scratch = new ScratchDirectory();
        var log = Path.Combine(scratch.Path, "broken.jsonl");

        await File.WriteAllTextAsync(log, "{\"run_id\":\n");

        var (exitCode, _, error) = await RunAsync("score", "--reviews", log);

        Assert.Equal(SpecTraceCli.ExitFailure, exitCode);
        Assert.Contains("line 1", error, StringComparison.Ordinal);
    }

    private static ReviewRecord Case(string caseId, CaseDecision decision) =>
        new("rfc6902-3ff2234db6aa", caseId, decision, null, "Ann", At);

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = new CliHost(new NoNetworkHandler(), _ => null, PromptSet.Embedded, output, error);

        var exitCode = await SpecTraceCli.RunAsync(arguments, host, CancellationToken.None);

        return (exitCode, output.ToString(), error.ToString());
    }
}
