using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

public sealed class OfflineReplayTests
{
    private static readonly string CommittedCache = Path.Combine(AppContext.BaseDirectory, "cache");

    private static async Task<(ExtractionRunResult Result, NoNetworkHandler Network)> ReplayAsync()
    {
        var network = new NoNetworkHandler();
        using var httpClient = new HttpClient(network);

        var client = LlmClientFactory.Create(httpClient, CommittedCache, offline: true, apiKey: null);
        var result = await ExtractionRun.ExecuteAsync(
            Corpus.Path,
            client,
            LlmClientFactory.DefaultModel,
            CancellationToken.None);

        return (result, network);
    }

    [Fact]
    public async Task TheExtractionReplaysFromTheCommittedCacheWithNoKeyAndNoNetwork()
    {
        var (result, network) = await ReplayAsync();

        Assert.True(result.Response.FromCache);
        Assert.Equal(0, network.Attempts);
    }

    [Fact]
    public async Task AtLeastFiveRequirementsAreVerifiedEndToEnd()
    {
        var (result, _) = await ReplayAsync();

        Assert.True(
            result.Outcome.Register.Count >= 5,
            $"Only {result.Outcome.Register.Count} requirements verified; M1 task 3 requires at least 5.");
    }

    [Fact]
    public async Task EveryReplayedRequirementsRawSpanNormalisesExactlyToItsQuote()
    {
        var (result, _) = await ReplayAsync();

        Assert.NotEmpty(result.Outcome.Register);

        foreach (var requirement in result.Outcome.Register)
        {
            var raw = Corpus.Raw[requirement.Span.Start..requirement.Span.End];

            Assert.Equal(TextNormalizer.Normalize(requirement.Text), TextNormalizer.Normalize(raw));
        }
    }

    [Fact]
    public async Task NoReplayedRequirementFailedVerification()
    {
        var (result, _) = await ReplayAsync();
        var outcome = result.Outcome;

        Assert.All(outcome.Register, requirement => Assert.Equal(Verification.Exact, requirement.Verification));
        Assert.True(
            outcome.Register.Count + outcome.Rejected.Count + outcome.Decisions.Count <= result.CandidateCount,
            "More outcomes than claims: something was invented between the model and the register.");
    }

    [Fact]
    public async Task TwoReplaysProduceIdenticalRequirementIdsAndIdenticalFiles()
    {
        var (first, _) = await ReplayAsync();
        var (second, _) = await ReplayAsync();

        Assert.Equal(first.RunId, second.RunId);
        Assert.Equal(
            first.Outcome.Register.Select(requirement => requirement.Id),
            second.Outcome.Register.Select(requirement => requirement.Id));
        Assert.Equal(
            first.Outcome.Register.Count,
            first.Outcome.Register.Select(requirement => requirement.Id).Distinct().Count());

        using var firstRun = new ScratchDirectory();
        using var secondRun = new ScratchDirectory();
        await RunArtifacts.WriteAsync(first.Outcome, firstRun.Path, CancellationToken.None);
        await RunArtifacts.WriteAsync(second.Outcome, secondRun.Path, CancellationToken.None);

        foreach (var file in new[] { RunArtifacts.RequirementsFile, RunArtifacts.RejectedQuotesFile, RunArtifacts.DecisionsFile })
        {
            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(firstRun.Path, file), CancellationToken.None),
                await File.ReadAllBytesAsync(Path.Combine(secondRun.Path, file), CancellationToken.None));
        }
    }
}
