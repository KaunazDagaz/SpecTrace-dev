using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

public sealed class RunCatalogTests
{
    [Fact]
    public async Task TheReferenceRunReadsBackIntoTheRegisterCasesAndQueueThePipelineWrote()
    {
        var contents = await RunReader.ReadAsync(Repository.PathTo("runs", "reference"), CancellationToken.None);

        Assert.Equal("rfc6902-3ff2234db6aa", contents.Manifest.RunId);
        Assert.Equal("rfc6902", contents.Manifest.DocumentId);
        Assert.Equal(12, contents.Register.Count);
        Assert.Equal(23, contents.Cases.Count);
        Assert.Equal(4, contents.DecisionQueue.Count);
        Assert.Empty(contents.Rejected);
        Assert.All(contents.Cases, testCase => Assert.Equal(ReviewStatus.Proposed, testCase.Status));
        Assert.Equal(
            ["quote_found_more_than_once", "quote_found_more_than_once", "same_quote_claimed_with_different_readings", "generation_blocked"],
            contents.DecisionQueue.Select(decision => decision.Reason));
    }

    [Fact]
    public async Task AnArtifactWithAnUnexpectedFieldIsRefusedRatherThanPartlyRead()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);

        ReviewWorkspace.EditTestCases(workspace.Reference, cases => cases[0]!["reviewer_notes"] = "added by hand");

        var catalog = new RunCatalog(workspace, TimeProvider.System);
        var exception = await Assert.ThrowsAsync<RunNotAvailableException>(() => catalog.LoadAsync(Workspace.ReferenceKey, CancellationToken.None));

        Assert.Contains("reviewer_notes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASourceDocumentThatNoLongerHoldsARequirementAtItsSpanIsRefusedForReview()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var corpus = Path.Combine(scratch.Path, "corpus");
        var raw = await File.ReadAllTextAsync(Repository.PathTo("corpus", "rfc6902.txt"));

        Directory.CreateDirectory(corpus);
        await File.WriteAllTextAsync(Path.Combine(corpus, "rfc6902.txt"), "Shifted by one line.\n" + raw);

        var catalog = new RunCatalog(workspace with { Corpus = corpus }, TimeProvider.System);
        var exception = await Assert.ThrowsAsync<RunNotAvailableException>(() => catalog.LoadAsync(Workspace.ReferenceKey, CancellationToken.None));

        Assert.Contains("does not hold requirement", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreadableReviewLogShowsNoReviewedViewAtAll()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var log = ReviewWorkspace.LogOf(workspace);

        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        await File.WriteAllTextAsync(log, "{\"half\": ");

        var catalog = new RunCatalog(workspace, TimeProvider.System);
        var exception = await Assert.ThrowsAsync<RunNotAvailableException>(() => catalog.LoadAsync(Workspace.ReferenceKey, CancellationToken.None));

        Assert.Contains("review log cannot be read", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("reference", true)]
    [InlineData("rfc6902-3ff2234db6aa", true)]
    [InlineData("fictional-gsr-bfc2a19318a1", true)]
    [InlineData("../reference", false)]
    [InlineData("rfc6902", false)]
    [InlineData("RFC6902-3ff2234db6aa", false)]
    [InlineData("rfc6902-3ff2234db6aa/..", false)]
    [InlineData("", false)]
    public void OnlyTheReferenceKeyAndWellFormedRunIdsNameARun(string key, bool isRunKey)
    {
        Assert.Equal(isRunKey, Workspace.IsRunKey(key));
    }
}
