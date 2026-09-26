using System.Text;
using System.Text.Json;
using SpecTrace.Cli;

namespace SpecTrace.Pipeline.Tests;

public sealed class BaselineArmTests
{
    [Fact]
    public async Task TheBaselineRunReplaysOfflineWithoutASingleNetworkRequestAndKeepsTheAnswerVerbatim()
    {
        using var run = await CliRun.ExecuteAsync(
            OfflineEndToEndRuns.OfflineByVariable,
            PromptSet.Embedded,
            "--arm",
            BaselineRun.Arm);

        Assert.True(run.ExitCode == SpecTraceCli.ExitSuccess, run.Error);
        Assert.Empty(run.Network.Attempted);
        Assert.Equal(
            [RunArtifacts.AnswerFile, RunArtifacts.ClaimsFile, RunArtifacts.ManifestFile],
            Directory.EnumerateFiles(run.OutputDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(
            RealAnswers.Baseline(Corpus.Raw),
            await File.ReadAllTextAsync(Path.Combine(run.OutputDirectory, RunArtifacts.AnswerFile), Encoding.UTF8));

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(run.OutputDirectory, RunArtifacts.ManifestFile)));

        Assert.Equal(1, manifest.RootElement.GetProperty("prompt_count").GetInt32());
        Assert.Equal(1, manifest.RootElement.GetProperty("cache_hits").GetInt32());
        Assert.StartsWith("rfc6902-baseline-", manifest.RootElement.GetProperty("run_id").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryClaimOfTheBaselineRunIsWrittenWithTheLineItCameFromAndItsOutcome()
    {
        using var run = await CliRun.ExecuteAsync(
            OfflineEndToEndRuns.OfflineByVariable,
            PromptSet.Embedded,
            "--arm",
            BaselineRun.Arm);

        using var claims = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(run.OutputDirectory, RunArtifacts.ClaimsFile)));
        var root = claims.RootElement;
        var first = root.GetProperty("claims")[0];

        Assert.Equal("STOP", root.GetProperty("finish_reason").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("parse_failure").ValueKind);
        Assert.Equal(16, root.GetProperty("claims").GetArrayLength());
        Assert.Equal("### Requirement 1", first.GetProperty("item").GetString());
        Assert.Equal("not_found", first.GetProperty("outcome").GetString());
        Assert.Contains(first.GetProperty("quote").GetString()!, first.GetProperty("source").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChangedBaselinePromptMakesTheOfflineRunFailWithACacheMissThatNamesIt()
    {
        using var edited = new MemoryStream(Encoding.UTF8.GetBytes(PromptFile.Baseline.Text.Replace("every requirement", "each requirement", StringComparison.Ordinal)));
        var changed = PromptFile.Read(PromptFile.Baseline.Name, edited);

        using var run = await CliRun.ExecuteAsync(
            OfflineEndToEndRuns.OfflineByVariable,
            PromptSet.Embedded with { Baseline = changed },
            "--arm",
            BaselineRun.Arm);

        Assert.Equal(SpecTraceCli.ExitFailure, run.ExitCode);
        Assert.Empty(run.Network.Attempted);
        Assert.Contains("Offline replay has no recorded response for the baseline call on rfc6902, prompt baseline.user.md", run.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(run.OutputDirectory), "A failed offline run left output behind.");
    }

    [Fact]
    public async Task OnlyTheRunCommandTakesAnArmAndOnlyTheTwoArmsItKnows()
    {
        using var unknown = await CliRun.ExecuteAsync(OfflineEndToEndRuns.OfflineByVariable, PromptSet.Embedded, "--arm", "chat");

        Assert.Equal(SpecTraceCli.ExitUsage, unknown.ExitCode);
        Assert.Contains("run takes --arm pipeline (the default) or --arm baseline", unknown.Error, StringComparison.Ordinal);
    }
}
