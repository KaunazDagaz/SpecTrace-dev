using SpecTrace.Cli;
using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

public sealed class GoldFileTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private static readonly string Document = Lf("""
        1.  Widgets

           A widget MUST have a name.

        2.  Gadgets

           The gadget MUST exist before use.

        3.  Gizmos

           The gadget MUST exist before use.
        """);

    private static readonly string Filled = Lf($"""
        document: widgets
        annotation_rules_commit: {Commit}
        annotator: Annotator
        annotated_at: 2026-09-28
        candidates:
        - candidate: 1
          sentence: >-
            A widget MUST have a name.
          decision: keep
          obligations:
          - quote: >-
              A widget MUST have a name
            modality: MUST
            testability: testable
            section:
        - candidate: 2
          sentence: >-
            The gadget MUST exist before use.
          decision: keep
          obligations:
          - quote: >-
              The gadget MUST exist
              before use
            modality: MUST
            testability: testable
            section: "2"
        - candidate: 3
          sentence: >-
            The gadget MUST exist before use.
          decision: drop
          obligations:
          - quote: >-
            modality:
            testability:
            section:
        """);

    [Fact]
    public void ParsingKeepsEveryFieldAndTheLineOfEveryCandidateAndObligation()
    {
        var file = GoldFile.Parse(Filled);

        Assert.Equal(("widgets", Commit, "Annotator", "2026-09-28"), (file.Document, file.RulesCommit, file.Annotator, file.AnnotatedAt));
        Assert.Equal([6, 16, 27], file.Candidates.Select(candidate => candidate.Line));
        Assert.Equal([11], file.Candidates[0].Obligations.Select(obligation => obligation.Line));
        Assert.Equal("The gadget MUST exist before use", file.Candidates[1].Obligations[0].Quote);
        Assert.Equal("2", file.Candidates[1].Obligations[0].Section);
        Assert.True(Assert.Single(file.Candidates[2].Obligations).IsBlank);
    }

    [Fact]
    public async Task AFileThatPassesEveryCheckLoadsAsAGoldStandard()
    {
        using var scratch = new ScratchDirectory();
        var (gold, document) = await WriteAsync(scratch, Filled);

        var standard = await GoldFile.LoadAsync(gold, document, Commit, CancellationToken.None);

        Assert.Equal(["1", "2"], standard.Requirements.Select(requirement => requirement.Section));
        Assert.Equal([Testability.Testable, Testability.Testable], standard.Requirements.Select(requirement => requirement.Testability));
    }

    [Fact]
    public async Task OneQuoteThatIsNotInTheDocumentRejectsTheWholeFileWithItsLine()
    {
        using var scratch = new ScratchDirectory();
        var (gold, document) = await WriteAsync(
            scratch,
            Filled.Replace("      A widget MUST have a name\n", "      A widget MUST have a title\n", StringComparison.Ordinal));

        var rejected = await Assert.ThrowsAsync<InvalidGoldFileException>(
            () => GoldFile.LoadAsync(gold, document, Commit, CancellationToken.None));

        var problem = Assert.Single(rejected.Problems);
        Assert.Equal(11, problem.Line);
        Assert.StartsWith("line 11: candidate 1, obligation 1: the quote is not in the document", InvalidGoldFileException.Describe(problem), StringComparison.Ordinal);
    }

    [Fact]
    public void AQuotePastedAtTheIndentationOfItsKeyIsASyntaxErrorReportedWithItsLine()
    {
        var misindented = Filled.Replace("      A widget MUST have a name\n", "    A widget MUST have a name\n", StringComparison.Ordinal);

        var rejected = Assert.Throws<InvalidGoldFileException>(() => GoldFile.Parse(misindented));

        Assert.InRange(Assert.Single(rejected.Problems).Line, 11, 14);
    }

    [Fact]
    public void AFieldHoldingAListInsteadOfOneValueIsRejected()
    {
        var rejected = Assert.Throws<InvalidGoldFileException>(
            () => GoldFile.Parse(Filled.Replace("  decision: keep\n", "  decision: [keep]\n", StringComparison.Ordinal)));

        Assert.Contains("'decision' holds a list or mapping, not a single value", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACandidateNumberThatIsNotANumberIsRejected()
    {
        var rejected = Assert.Throws<InvalidGoldFileException>(
            () => GoldFile.Parse(Filled.Replace("- candidate: 1\n", "- candidate: one\n", StringComparison.Ordinal)));

        Assert.Equal(6, Assert.Single(rejected.Problems).Line);
    }

    [Fact]
    public async Task TheCheckCommandListsEveryProblemInTheUnfilledWorksheetAndExitsWithFailure()
    {
        var worksheet = Repository.PathTo("corpus", "worksheets", "rfc6902.worksheet.yaml");
        var candidates = GoldFile.Parse(await File.ReadAllTextAsync(worksheet)).Candidates.Count;

        var (exitCode, output, error) = await AnnotationWorksheetTests.RunAsync(
            "score", "--gold", worksheet, "--document", Corpus.Path);

        Assert.Equal(SpecTraceCli.ExitFailure, exitCode);
        Assert.Contains($"candidates     {candidates} in the file: 0 keep, 0 drop, {candidates} undecided", output, StringComparison.Ordinal);
        Assert.Equal(candidates, error.Split('\n').Count(line => line.Contains("has no decision: keep or drop", StringComparison.Ordinal)));
        Assert.Contains("One problem rejects the whole file.", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCheckCommandReportsAMissingGoldFileAsNotLoaded()
    {
        using var scratch = new ScratchDirectory();

        var (exitCode, _, error) = await AnnotationWorksheetTests.RunAsync(
            "score", "--gold", Path.Combine(scratch.Path, "absent.gold.yaml"), "--document", Corpus.Path);

        Assert.Equal(SpecTraceCli.ExitFailure, exitCode);
        Assert.Contains("The gold file does not load.", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCheckCommandNeedsTheDocument()
    {
        var (exitCode, _, error) = await AnnotationWorksheetTests.RunAsync("score", "--gold", "corpus/gold/rfc6902.gold.yaml");

        Assert.Equal(SpecTraceCli.ExitUsage, exitCode);
        Assert.Contains("score --gold needs --document <path>.", error, StringComparison.Ordinal);
    }

    private static async Task<(string Gold, string Document)> WriteAsync(ScratchDirectory scratch, string gold)
    {
        var goldPath = Path.Combine(scratch.Path, "widgets.gold.yaml");
        var documentPath = Path.Combine(scratch.Path, "widgets.txt");

        await File.WriteAllTextAsync(goldPath, gold);
        await File.WriteAllTextAsync(documentPath, Document);

        return (goldPath, documentPath);
    }

    private static string Lf(string text) => text.ReplaceLineEndings("\n") + "\n";
}
