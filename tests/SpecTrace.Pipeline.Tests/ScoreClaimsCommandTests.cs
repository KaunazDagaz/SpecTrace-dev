using System.Text.Json;
using SpecTrace.Cli;

namespace SpecTrace.Pipeline.Tests;

public sealed class ScoreClaimsCommandTests
{
    private static readonly string FrontMatter = """
        ---
        document: rfc6902.txt
        captured_at: 2026-09-26T13:05+03:00
        interface: gemini.google.com
        model: Gemini 3.5 Flash
        mode: default
        input: file attachment
        share_link: https://g.co/gemini/share/abc123
        prompt: |
          List every requirement in the following specification. For each
          one, quote the exact sentence it comes from, then write test cases
          for it with a title, input and expected result.
        ---

        """.ReplaceLineEndings("\n");

    private static readonly string Answer = """
        ### 1. The op member
        * **Exact Quote:** "Operation objects MUST have exactly one "op" member, whose value indicates the operation to perform."

        ### 2. The op member, requoted
        * **Exact Quote:** "Operation objects MUST have exactly one 'op' member, whose value indicates the operation to perform."

        ### 3. Removal
        * **Exact Quote:** "The target location MUST exist for the operation to be successful."

        ### 4. A requirement with no quote
        * **Test Case Title:** Something
        """.ReplaceLineEndings("\n");

    [Fact]
    public async Task AnExternallyProducedAnswerIsScoredByTheSameParserAndVerifierAndItsMetricsAreWritten()
    {
        using var scratch = new ScratchDirectory();
        var transcript = Path.Combine(scratch.Path, "a0", "rfc6902.md");
        Directory.CreateDirectory(Path.GetDirectoryName(transcript)!);
        await File.WriteAllTextAsync(transcript, FrontMatter + Answer);

        var (exitCode, output, error, network) = await ScoreAsync(scratch.Path, transcript);

        Assert.True(exitCode == SpecTraceCli.ExitSuccess, error);
        Assert.Empty(network.Attempted);
        Assert.Contains("not located    2 of 4, 50.0%", output, StringComparison.Ordinal);

        using var metrics = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(scratch.Path, "rfc6902-chat-2026-09-26.metrics.json")));
        var root = metrics.RootElement;
        var claimed = root.GetProperty("claimed");

        Assert.Equal("A0", root.GetProperty("arm").GetString());
        Assert.False(root.GetProperty("reproducible").GetBoolean());
        Assert.Equal("Gemini 3.5 Flash", root.GetProperty("model").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("cost").ValueKind);
        Assert.Equal("a0/rfc6902.md", root.GetProperty("capture").GetProperty("transcript").GetString());
        Assert.Equal(4, claimed.GetProperty("claims").GetInt32());
        Assert.Equal(1, claimed.GetProperty("quotes_found_once").GetInt32());
        Assert.Equal(1, claimed.GetProperty("quotes_found_more_than_once").GetInt32());
        Assert.Equal(1, claimed.GetProperty("quotes_not_found").GetInt32());
        Assert.Equal(1, claimed.GetProperty("claims_without_quote").GetInt32());
        Assert.Equal(0.5, claimed.GetProperty("not_located_share").GetDouble());
    }

    [Fact]
    public async Task ATranscriptWithPlaceholdersIsNotScoredAndEveryProblemIsNamed()
    {
        using var scratch = new ScratchDirectory();
        var transcript = Path.Combine(scratch.Path, "rfc6902.md");
        await File.WriteAllTextAsync(
            transcript,
            FrontMatter.Replace("model: Gemini 3.5 Flash", "model: <model name as shown in the interface>", StringComparison.Ordinal) + Answer);

        var (exitCode, _, error, _) = await ScoreAsync(scratch.Path, transcript);

        Assert.Equal(SpecTraceCli.ExitFailure, exitCode);
        Assert.Contains("'model' is still the placeholder <model name as shown in the interface>", error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(scratch.Path, "*.metrics.json"));
    }

    [Fact]
    public async Task AMissingTranscriptIsReportedRatherThanScoredAsEmpty()
    {
        using var scratch = new ScratchDirectory();

        var (exitCode, _, error, _) = await ScoreAsync(scratch.Path, Path.Combine(scratch.Path, "absent.md"));

        Assert.Equal(SpecTraceCli.ExitFailure, exitCode);
        Assert.Contains("Nothing was scored.", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScoringAgainstAGoldStandardIsStillReportedAsNotImplemented()
    {
        var network = new NoNetworkHandler();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = new CliHost(network, _ => null, PromptSet.Embedded, output, error);

        var exitCode = await SpecTraceCli.RunAsync(["score", "--run", "x", "--gold", "y"], host, CancellationToken.None);

        Assert.Equal(SpecTraceCli.ExitUsage, exitCode);
        Assert.Contains("SPEC-12", error.ToString(), StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output, string Error, NoNetworkHandler Network)> ScoreAsync(string outputDirectory, string transcript)
    {
        var network = new NoNetworkHandler();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = new CliHost(network, _ => null, PromptSet.Embedded, output, error);

        var exitCode = await SpecTraceCli.RunAsync(
            ["score", "--claims", transcript, "--document", Repository.PathTo("corpus", "rfc6902.txt"), "--out", outputDirectory],
            host,
            CancellationToken.None);

        return (exitCode, output.ToString(), error.ToString(), network);
    }
}
