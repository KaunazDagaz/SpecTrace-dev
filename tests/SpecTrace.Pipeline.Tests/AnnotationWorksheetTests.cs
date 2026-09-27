using System.Text;
using SpecTrace.Cli;
using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

public sealed class AnnotationWorksheetTests
{
    private static readonly string CommittedWorksheet =
        Repository.PathTo("corpus", "worksheets", "rfc6902.worksheet.yaml");

    [Fact]
    public void TheCommittedWorksheetIsExactlyWhatTheKeywordScanWrites()
    {
        var expected = Encoding.UTF8.GetBytes(
            AnnotationWorksheet.Render(Corpus.DocumentId, KeywordSentences.Find(Corpus.Raw)));

        Assert.Equal(expected, File.ReadAllBytes(CommittedWorksheet));
    }

    [Fact]
    public void TheCommittedWorksheetLeavesEveryAnnotatorFieldEmpty()
    {
        var worksheet = GoldFile.Parse(File.ReadAllText(CommittedWorksheet));

        Assert.Equal(Corpus.DocumentId, worksheet.Document);
        Assert.Equal((string.Empty, string.Empty, string.Empty), (worksheet.RulesCommit, worksheet.Annotator, worksheet.AnnotatedAt));
        Assert.NotEmpty(worksheet.Candidates);
        Assert.All(worksheet.Candidates, candidate =>
        {
            Assert.Equal(string.Empty, candidate.Decision);
            Assert.True(Assert.Single(candidate.Obligations).IsBlank);
        });
    }

    [Fact]
    public void TheWorksheetReadsBackAsTheSentencesTheScanFound()
    {
        var scan = KeywordSentences.Find(Corpus.Raw);
        var worksheet = GoldFile.Parse(AnnotationWorksheet.Render(Corpus.DocumentId, scan));

        Assert.Equal(
            scan.Select(sentence => (sentence.Number, sentence.Sentence)),
            worksheet.Candidates.Select(candidate => (candidate.Number, candidate.Sentence)));
    }

    [Fact]
    public void AQuotePastedIntoTheTemplateReadsBackVerbatimWhateverQuotationMarksAndColonsItHolds()
    {
        const string Quote = "The \"from\" location's value: MUST NOT be # a prefix";
        var raw = """
            1.  Rules

               The "from" location's value: MUST NOT be # a prefix.
            """.ReplaceLineEndings("\n") + "\n";

        var filled = AnnotationWorksheet
            .Render("rules", KeywordSentences.Find(raw))
            .Replace("  decision:\n", "  decision: keep\n", StringComparison.Ordinal)
            .Replace("  - quote: >-\n    modality:\n", $"  - quote: >-\n      {Quote}\n    modality: MUST_NOT\n", StringComparison.Ordinal);

        var obligation = Assert.Single(Assert.Single(GoldFile.Parse(filled).Candidates).Obligations);

        Assert.Equal(Quote, obligation.Quote);
        Assert.Equal("MUST_NOT", obligation.Modality);
    }

    [Fact]
    public async Task TheWorksheetCommandWritesTheWorksheetAndNeverWritesOverAnExistingFile()
    {
        using var scratch = new ScratchDirectory();
        var worksheet = Path.Combine(scratch.Path, "worksheets", "rfc6902.worksheet.yaml");

        var (first, output, _) = await RunAsync("score", "--worksheet", worksheet, "--document", Corpus.Path);

        Assert.Equal(SpecTraceCli.ExitSuccess, first);
        Assert.Contains("no model was called", output, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllBytes(CommittedWorksheet), File.ReadAllBytes(worksheet));

        await File.WriteAllTextAsync(worksheet, "annotated by hand");
        var (second, _, error) = await RunAsync("score", "--worksheet", worksheet, "--document", Corpus.Path);

        Assert.Equal(SpecTraceCli.ExitFailure, second);
        Assert.Contains("No worksheet was written.", error, StringComparison.Ordinal);
        Assert.Equal("annotated by hand", await File.ReadAllTextAsync(worksheet));
    }

    internal static async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        var network = new NoNetworkHandler();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = new CliHost(network, _ => null, PromptSet.Embedded, output, error);

        var exitCode = await SpecTraceCli.RunAsync(args, host, CancellationToken.None);

        Assert.Empty(network.Attempted);
        return (exitCode, output.ToString(), error.ToString());
    }
}
