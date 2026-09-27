using System.Text.Json;
using SpecTrace.Cli;

namespace SpecTrace.Pipeline.Tests;

public sealed class ScoreRunCommandTests
{
    private static readonly string Gold = Repository.PathTo("corpus", "gold", "rfc6902.gold.yaml");

    [Theory]
    [InlineData("rfc6902-3ff2234db6aa")]
    [InlineData("rfc6902-baseline-4227a0d51f3d")]
    [InlineData("rfc6902-chat-2026-09-26")]
    public async Task ScoringOneRunOfflineWritesExactlyTheCommittedMetricsFileForThatRun(string runId)
    {
        using var scratch = new ScratchDirectory();

        var (exitCode, output, error, network) = await ScoreAsync(scratch.Path, "--run", runId, "--gold", Gold);

        Assert.True(exitCode == SpecTraceCli.ExitSuccess, error);
        Assert.Empty(network.Attempted);
        Assert.Contains("precision", output, StringComparison.Ordinal);
        Assert.Equal([$"{runId}.metrics.json"], Directory.EnumerateFiles(scratch.Path).Select(Path.GetFileName));
        Assert.Equal(
            await File.ReadAllTextAsync(Repository.PathTo("experiments", $"{runId}.metrics.json")),
            await File.ReadAllTextAsync(Path.Combine(scratch.Path, $"{runId}.metrics.json")));
    }

    [Fact]
    public async Task ScoringAChatTranscriptWithTheGoldStandardWritesTheSameMetricsFileAsTheHeadline()
    {
        using var scratch = new ScratchDirectory();
        var transcript = Path.Combine(scratch.Path, "a0", "rfc6902.md");
        Directory.CreateDirectory(Path.GetDirectoryName(transcript)!);
        File.Copy(Repository.PathTo("experiments", "a0", "rfc6902.md"), transcript);

        var network = new NoNetworkHandler();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = new CliHost(network, _ => null, PromptSet.Embedded, output, error);

        var exitCode = await SpecTraceCli.RunAsync(
            [
                "score",
                "--claims", transcript,
                "--document", Repository.PathTo("corpus", "rfc6902.txt"),
                "--gold", Gold,
                "--out", scratch.Path,
            ],
            host,
            CancellationToken.None);

        Assert.True(exitCode == SpecTraceCli.ExitSuccess, error.ToString());
        Assert.Equal(
            await File.ReadAllTextAsync(Repository.PathTo("experiments", "rfc6902-chat-2026-09-26.metrics.json")),
            await File.ReadAllTextAsync(Path.Combine(scratch.Path, "rfc6902-chat-2026-09-26.metrics.json")));
    }

    [Fact]
    public async Task TheMetricsFileHoldsEveryFieldReqExp03Lists()
    {
        using var scratch = new ScratchDirectory();

        var (exitCode, _, error, _) = await ScoreAsync(scratch.Path, "--run", "rfc6902-3ff2234db6aa", "--gold", Gold);

        Assert.True(exitCode == SpecTraceCli.ExitSuccess, error);

        using var metrics = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(scratch.Path, "rfc6902-3ff2234db6aa.metrics.json")));
        var root = metrics.RootElement;
        var quality = root.GetProperty("quality");

        foreach (var view in new[] { "claimed", "delivered" })
        {
            var score = quality.GetProperty(view);

            foreach (var field in new[] { "precision", "recall", "f1", "modality_accuracy" })
            {
                Assert.Equal(JsonValueKind.Number, score.GetProperty(field).ValueKind);
            }
        }

        Assert.Equal(JsonValueKind.Number, root.GetProperty("claimed").GetProperty("verification_rate").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("cost").GetProperty("input_tokens").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("cost").GetProperty("output_tokens").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("cost").GetProperty("cache_hit_rate").ValueKind);
        Assert.Equal(19, quality.GetProperty("gold_standard").GetProperty("requirements").GetInt32());
    }

    [Fact]
    public async Task AnArmThatStatesNoModalityReportsModalityAccuracyAsNotApplicableWithTheReason()
    {
        using var scratch = new ScratchDirectory();

        var (exitCode, _, error, _) = await ScoreAsync(scratch.Path, "--run", "rfc6902-baseline-4227a0d51f3d", "--gold", Gold);

        Assert.True(exitCode == SpecTraceCli.ExitSuccess, error);

        using var metrics = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(scratch.Path, "rfc6902-baseline-4227a0d51f3d.metrics.json")));
        var claimed = metrics.RootElement.GetProperty("quality").GetProperty("claimed");

        Assert.Equal(JsonValueKind.Null, claimed.GetProperty("modality_accuracy").ValueKind);
        Assert.Equal(QualityReport.ModalityNotStated, claimed.GetProperty("modality_not_applicable").GetString());
    }

    [Fact]
    public async Task ARunTheCacheCannotReplayIsRefusedAndTheRunsThatCanAreNamed()
    {
        using var scratch = new ScratchDirectory();

        var (exitCode, _, error, _) = await ScoreAsync(scratch.Path, "--run", "rfc6902-000000000000", "--gold", Gold);

        Assert.Equal(SpecTraceCli.ExitFailure, exitCode);
        Assert.Contains("rfc6902-3ff2234db6aa", error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(scratch.Path));
    }

    [Fact]
    public async Task ARunOverAnotherDocumentThanTheGoldStandardAnnotatesIsRefused()
    {
        using var scratch = new ScratchDirectory();

        var (exitCode, _, error, _) = await ScoreAsync(scratch.Path, "--run", "rfc10050-9759f1bdfc79", "--gold", Gold);

        Assert.Equal(SpecTraceCli.ExitFailure, exitCode);
        Assert.Contains("rfc6902", error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(scratch.Path));
    }

    [Fact]
    public async Task AGoldFileThatDoesNotExistFailsAndScoresNothing()
    {
        using var scratch = new ScratchDirectory();

        var (exitCode, _, error, _) = await ScoreAsync(
            scratch.Path,
            "--run", "rfc6902-3ff2234db6aa",
            "--gold", Path.Combine(scratch.Path, "missing.gold.yaml"));

        Assert.Equal(SpecTraceCli.ExitFailure, exitCode);
        Assert.Contains("Nothing was scored.", error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(scratch.Path));
    }

    [Fact]
    public async Task AGoldFileThatDoesNotLoadIsRefusedWithItsProblems()
    {
        using var scratch = new ScratchDirectory();
        var broken = Path.Combine(scratch.Path, "..", $"{Path.GetFileName(scratch.Path)}.gold.yaml");
        await File.WriteAllTextAsync(
            broken,
            (await File.ReadAllTextAsync(Gold)).Replace(AnnotationRules.FrozenCommit, "0000000000000000000000000000000000000000", StringComparison.Ordinal));

        try
        {
            var (exitCode, _, error, _) = await ScoreAsync(scratch.Path, "--run", "rfc6902-3ff2234db6aa", "--gold", broken);

            Assert.Equal(SpecTraceCli.ExitFailure, exitCode);
            Assert.Contains("annotation_rules_commit", error, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(scratch.Path));
        }
        finally
        {
            File.Delete(broken);
        }
    }

    [Fact]
    public async Task ScoringARunNeedsAGoldStandard()
    {
        using var scratch = new ScratchDirectory();

        var (exitCode, _, error, _) = await ScoreAsync(scratch.Path, "--run", "rfc6902-3ff2234db6aa");

        Assert.Equal(SpecTraceCli.ExitUsage, exitCode);
        Assert.Contains("--gold", error, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output, string Error, NoNetworkHandler Network)> ScoreAsync(
        string scratch,
        params string[] arguments)
    {
        var transcripts = Path.Combine(scratch, "a0");
        Directory.CreateDirectory(transcripts);

        foreach (var transcript in Directory.EnumerateFiles(Repository.PathTo("experiments", "a0"), "*.md"))
        {
            File.Copy(transcript, Path.Combine(transcripts, Path.GetFileName(transcript)));
        }

        var network = new NoNetworkHandler();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = new CliHost(network, _ => null, PromptSet.Embedded, output, error);

        var exitCode = await SpecTraceCli.RunAsync(
            [
                "score",
                .. arguments,
                "--document", Repository.PathTo("corpus", "rfc6902.txt"),
                "--cache", Repository.PathTo("cache"),
                "--transcripts", transcripts,
                "--out", scratch,
            ],
            host,
            CancellationToken.None);

        return (exitCode, output.ToString(), error.ToString(), network);
    }
}
