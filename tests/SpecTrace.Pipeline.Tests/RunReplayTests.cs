using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

public sealed class RunReplayTests
{
    private const string TruncatedListLeadIn = "When the operation is applied, the target location MUST reference one of:";

    private static readonly string CommittedCache = Path.Combine(AppContext.BaseDirectory, "cache");

    private static readonly string[] AllArtifacts =
    [
        RunArtifacts.RequirementsFile,
        RunArtifacts.RejectedQuotesFile,
        RunArtifacts.DecisionsFile,
        RunArtifacts.TestCasesFile,
        RunArtifacts.MatrixFile,
        RunArtifacts.MatrixHtmlFile,
    ];

    private static async Task<(PipelineRunResult Run, NoNetworkHandler Network)> ReplayAsync()
    {
        var network = new NoNetworkHandler();
        using var httpClient = new HttpClient(network);

        var client = LlmClientFactory.Create(httpClient, CommittedCache, offline: true, apiKey: null);
        var run = await PipelineRun.ExecuteAsync(
            Corpus.Path,
            client,
            LlmClientFactory.DefaultModel,
            CancellationToken.None);

        return (run, network);
    }

    [Fact]
    public async Task TheFullRunReplaysFromTheCommittedCacheWithNoKeyAndNoNetwork()
    {
        var (run, network) = await ReplayAsync();

        Assert.True(run.Extraction.Response.FromCache);
        Assert.NotEmpty(run.Generations);
        Assert.All(run.Generations, generation => Assert.True(generation.Response.FromCache));
        Assert.Equal(0, network.Attempts);
    }

    [Fact]
    public async Task TheReplayedRunsWrittenArtifactsHoldTheInvariants()
    {
        var (run, _) = await ReplayAsync();
        using var directory = new ScratchDirectory();

        await RunArtifacts.WriteRunAsync(run, directory.Path, CancellationToken.None);

        ArtifactInvariants.AssertHold(directory.Path, Corpus.Raw);
    }

    [Fact]
    public async Task DeletingAndRegeneratingTheMatrixFromTheSameInputsGivesIdenticalBytes()
    {
        var (run, _) = await ReplayAsync();
        using var directory = new ScratchDirectory();

        await RunArtifacts.WriteRunAsync(run, directory.Path, CancellationToken.None);

        var matrixJson = Path.Combine(directory.Path, RunArtifacts.MatrixFile);
        var matrixHtml = Path.Combine(directory.Path, RunArtifacts.MatrixHtmlFile);
        var originalJson = await File.ReadAllBytesAsync(matrixJson, CancellationToken.None);
        var originalHtml = await File.ReadAllBytesAsync(matrixHtml, CancellationToken.None);

        File.Delete(matrixJson);
        File.Delete(matrixHtml);
        Assert.False(File.Exists(matrixJson) || File.Exists(matrixHtml));

        var (inputs, _) = await ReplayAsync();

        await RunArtifacts.WriteMatrixAsync(
            inputs.DocumentId,
            inputs.Register,
            inputs.Cases,
            inputs.HumanDecisions,
            inputs.DecisionQueue,
            directory.Path,
            CancellationToken.None);

        Assert.Equal(originalJson, await File.ReadAllBytesAsync(matrixJson, CancellationToken.None));
        Assert.Equal(originalHtml, await File.ReadAllBytesAsync(matrixHtml, CancellationToken.None));
    }

    [Fact]
    public async Task TwoReplaysWriteByteIdenticalArtifacts()
    {
        var (first, _) = await ReplayAsync();
        var (second, _) = await ReplayAsync();

        Assert.Equal(first.RunId, second.RunId);

        using var firstRun = new ScratchDirectory();
        using var secondRun = new ScratchDirectory();
        await RunArtifacts.WriteRunAsync(first, firstRun.Path, CancellationToken.None);
        await RunArtifacts.WriteRunAsync(second, secondRun.Path, CancellationToken.None);

        foreach (var file in AllArtifacts)
        {
            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(firstRun.Path, file), CancellationToken.None),
                await File.ReadAllBytesAsync(Path.Combine(secondRun.Path, file), CancellationToken.None));
        }
    }

    [Fact]
    public async Task ARemovedRequirementsCasesLandInTheOrphansSectionRatherThanBeingDropped()
    {
        var (run, _) = await ReplayAsync();
        var removed = run.Matrix.Rows.First(row => row.Status == CoverageStatus.Covered);
        var itsCases = removed.TestCaseIds;

        using var directory = new ScratchDirectory();
        await RunArtifacts.WriteMatrixAsync(
            run.DocumentId,
            run.Register.Where(requirement => requirement.Id != removed.RequirementId).ToList(),
            run.Cases,
            run.HumanDecisions,
            run.DecisionQueue,
            directory.Path,
            CancellationToken.None);

        var matrix = ArtifactInvariants.ReadMatrix(directory.Path);
        var orphans = matrix.GetProperty("orphans").EnumerateArray().ToList();

        Assert.NotEmpty(itsCases);
        Assert.Equal(itsCases, orphans.Select(ArtifactInvariants.Text("test_case_id")));
        Assert.All(orphans, orphan =>
            Assert.Equal([removed.RequirementId], ArtifactInvariants.Strings(orphan.GetProperty("missing_requirement_ids"))));
        Assert.DoesNotContain(
            removed.RequirementId,
            matrix.GetProperty("rows").EnumerateArray().Select(ArtifactInvariants.Text("requirement_id")));

        var html = await File.ReadAllTextAsync(Path.Combine(directory.Path, RunArtifacts.MatrixHtmlFile), CancellationToken.None);
        var orphanSection = html[html.IndexOf("id=\"orphans\"", StringComparison.Ordinal)..html.IndexOf("id=\"cases\"", StringComparison.Ordinal)];

        Assert.Contains($"Orphan test cases: {itsCases.Count}", orphanSection, StringComparison.Ordinal);
        Assert.All(itsCases, id => Assert.Contains($"<tr><td>{id}</td>", orphanSection, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheRecordedRunHasTheCountsItWasRecordedWith()
    {
        var (run, _) = await ReplayAsync();
        var rows = run.Matrix.Rows;

        Assert.Equal(12, run.Register.Count);
        Assert.Equal(12, run.Generations.Count);
        Assert.Equal(11, rows.Count(row => row.Status == CoverageStatus.Covered));
        Assert.Equal(1, rows.Count(row => row.Status == CoverageStatus.Gap));
        Assert.Equal(1, run.Generations.Count(generation => generation.BlockedReason is not null));
        Assert.Empty(run.Matrix.Orphans);

        Assert.Equal(23, run.Cases.Count);
        Assert.Equal(11, run.Cases.Count(testCase => testCase.Type == CaseType.Positive));
        Assert.Equal(12, run.Cases.Count(testCase => testCase.Type == CaseType.Negative));
        Assert.Equal(0, run.Cases.Count(testCase => testCase.Type == CaseType.Boundary));
        Assert.All(run.Cases, testCase => Assert.Equal(ReviewStatus.Proposed, testCase.Status));

        Assert.Equal(
            [
                QueuedDecision.QuoteFoundMoreThanOnce,
                QueuedDecision.QuoteFoundMoreThanOnce,
                QueuedDecision.ConflictingReadings,
                QueuedDecision.GenerationBlocked,
            ],
            run.DecisionQueue.Select(decision => decision.Reason));
    }

    [Fact]
    public async Task TheRecordedBlockedGenerationIsTheListLeadInWhoseItemsTheQuoteDoesNotCarry()
    {
        var (run, _) = await ReplayAsync();

        var requirement = run.Register.Single(requirement => requirement.Text == TruncatedListLeadIn);
        var decision = run.DecisionQueue.Single(decision => decision.Reason == QueuedDecision.GenerationBlocked);

        Assert.Equal(requirement.Id, decision.RequirementId);
        Assert.Equal("4.1", decision.Item.Section);
        Assert.False(string.IsNullOrWhiteSpace(decision.BlockedReason));
        Assert.Equal(CoverageStatus.Gap, run.Matrix.Rows.Single(row => row.RequirementId == requirement.Id).Status);
    }
}
