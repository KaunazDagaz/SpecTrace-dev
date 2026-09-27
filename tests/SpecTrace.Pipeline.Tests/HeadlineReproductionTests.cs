using SpecTrace.Cli;

namespace SpecTrace.Pipeline.Tests;

public sealed class HeadlineReproductionTests
{
    private const string CommandStart = "dotnet run --project src/SpecTrace.Cli -- ";

    private static readonly string Experiments = Repository.PathTo("experiments");

    [Fact]
    public async Task TheCommittedHeadlineAndEveryFileItGeneratesRegenerateOfflineByteForByte()
    {
        var command = CommittedCommand();

        using var scratch = new ScratchDirectory();
        var transcripts = Path.Combine(scratch.Path, "a0");
        Directory.CreateDirectory(transcripts);

        foreach (var transcript in Directory.EnumerateFiles(Path.Combine(Experiments, "a0"), "*.md"))
        {
            File.Copy(transcript, Path.Combine(transcripts, Path.GetFileName(transcript)));
        }

        var network = new NoNetworkHandler();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = new CliHost(network, _ => null, PromptSet.Embedded, output, error);

        var exitCode = await SpecTraceCli.RunAsync(
            [
                .. InRepository(command[CommandStart.Length..].Split(' ')),
                "--cache", Repository.PathTo("cache"),
                "--transcripts", transcripts,
                "--out", scratch.Path,
            ],
            host,
            CancellationToken.None);

        Assert.True(exitCode == SpecTraceCli.ExitSuccess, error.ToString());
        Assert.Empty(network.Attempted);
        Assert.Equal(GeneratedFiles(Experiments), GeneratedFiles(scratch.Path));

        foreach (var file in GeneratedFiles(Experiments))
        {
            var committed = await File.ReadAllTextAsync(Path.Combine(Experiments, file));
            var regenerated = await File.ReadAllTextAsync(Path.Combine(scratch.Path, file));

            regenerated = string.Join('\n', regenerated.Split('\n').Select(line =>
                line.StartsWith(SpecTraceCli.HeadlineCommandPrefix, StringComparison.Ordinal) ? command : line));

            Assert.Equal(committed, regenerated);
        }
    }

    [Fact]
    public void CiRunsExactlyTheCommandTheHeadlineSaysRebuildsIt()
    {
        var workflow = File.ReadAllLines(Repository.PathTo(".github", "workflows", "ci.yml"));

        Assert.Contains(workflow, line => line.Trim() == $"run: {CommittedCommand()}");
    }

    [Fact]
    public void TheHeadlineIsRebuiltAgainstTheCommittedGoldStandard()
    {
        Assert.EndsWith($" {SpecTraceCli.GoldOption} corpus/gold/rfc6902.gold.yaml", CommittedCommand(), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCommittedMetricsAndQualityFileIsNamedInTheHeadlineSoNoneIsStale()
    {
        var headline = File.ReadAllText(Path.Combine(Experiments, ExperimentArtifacts.HeadlineFile));

        foreach (var file in Directory.EnumerateFiles(Experiments, "*.metrics.json")
            .Concat(Directory.EnumerateFiles(Experiments, "*.quality.md"))
            .Append(Path.Combine(Experiments, QualityReport.ChunkingFile))
            .Select(Path.GetFileName))
        {
            Assert.Contains($"[{file}]({file})", headline, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> InRepository(string[] arguments)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            yield return arguments[index];

            if (arguments[index] is "--documents" or SpecTraceCli.GoldOption)
            {
                yield return string.Join(',', arguments[++index].Split(',').Select(path => Repository.PathTo(path.Split('/'))));
            }
        }
    }

    private static string CommittedCommand() =>
        File.ReadAllLines(Path.Combine(Experiments, ExperimentArtifacts.HeadlineFile))
            .Single(line => line.StartsWith(SpecTraceCli.HeadlineCommandPrefix, StringComparison.Ordinal));

    private static List<string> GeneratedFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.metrics.json")
            .Concat(Directory.EnumerateFiles(directory, "*.quality.md"))
            .Append(Path.Combine(directory, ExperimentArtifacts.HeadlineFile))
            .Append(Path.Combine(directory, QualityReport.ChunkingFile))
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToList();
}
