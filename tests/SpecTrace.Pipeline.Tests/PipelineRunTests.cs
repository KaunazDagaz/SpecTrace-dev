using System.Text.Json;
using SpecTrace.Core;
using SpecTrace.Llm;

namespace SpecTrace.Pipeline.Tests;

public sealed class PipelineRunTests
{
    private const string AddValue =
        "The operation object MUST contain a \"value\" member whose content specifies the value to be added.";

    private const string ReplaceValue =
        "The operation object MUST contain a \"value\" member whose content specifies the replacement value.";

    private const string MembersIgnored =
        "Members that are not explicitly defined for the operation in question MUST be ignored";

    private const string OpMember =
        "Operation objects MUST have exactly one \"op\" member, whose value indicates the operation to perform.";

    private const string TargetExists = "The target location MUST exist for the operation to be successful.";

    private const string BlockedReason = "The quote states what must be present but not what an implementation does when it is <absent> & \"missing\".";

    private static readonly string Extraction = ExtractionAnswer(
        ("MUST", AddValue, "testable", null),
        ("MUST", ReplaceValue, "testable", null),
        ("MUST", MembersIgnored, "needs_human_decision", "Whether a member is defined depends on the operation."),
        ("MUST", OpMember, "not_testable", "Holds by construction of a valid patch."),
        ("MUST", TargetExists, "testable", null));

    [Fact]
    public async Task OnlyTestableRequirementsAreSentForGenerationEachWithItsSectionAndQuoteAlone()
    {
        var (run, model) = await RunAsync();

        var testable = run.Register.Where(requirement => requirement.Testability == Testability.Testable).ToList();

        Assert.Equal(2, testable.Count);
        Assert.Equal(
            testable.Select(TestCaseGenerator.UserPromptFor).Order(StringComparer.Ordinal),
            model.GenerationRequests.Select(request => request.UserPrompt).Order(StringComparer.Ordinal));
        Assert.All(model.GenerationRequests, request =>
            Assert.DoesNotContain("Request for Comments: 6902", request.UserPrompt, StringComparison.Ordinal));
        Assert.Equal(2, run.Generations.Count);
    }

    [Fact]
    public async Task NoCaseNamesARequirementTheModelFlagged()
    {
        var (run, _) = await RunAsync();

        var flagged = run.Register
            .Where(requirement => requirement.Testability != Testability.Testable)
            .Select(requirement => requirement.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(2, flagged.Count);
        Assert.All(run.Cases, testCase => Assert.DoesNotContain(testCase.RequirementIds, flagged.Contains));
    }

    [Theory]
    [InlineData(MembersIgnored, QueuedDecision.ModelFlaggedNeedsHumanDecision, "4")]
    [InlineData(OpMember, QueuedDecision.ModelFlaggedNotTestable, "4")]
    public async Task ARequirementTheModelFlaggedGoesToTheDecisionQueueAndStaysAGap(
        string quote,
        string reason,
        string section)
    {
        var (run, _) = await RunAsync();

        var requirement = run.Register.Single(requirement => requirement.Text == quote);
        var decision = run.DecisionQueue.Single(decision => decision.RequirementId == requirement.Id);

        Assert.Equal(reason, decision.Reason);
        Assert.Equal(quote, decision.Item.Quote);
        Assert.Equal(section, decision.Item.Section);
        Assert.EndsWith("?", decision.Item.Question, StringComparison.Ordinal);
        Assert.Null(decision.Item.Resolution);
        Assert.Equal($"DQ-{requirement.Id["REQ-".Length..]}", decision.Item.Id);
        Assert.Equal(requirement.TestabilityNote, Assert.Single(decision.Claims).TestabilityNote);
        Assert.Equal(CoverageStatus.Gap, run.Matrix.Rows.Single(row => row.RequirementId == requirement.Id).Status);
    }

    [Fact]
    public async Task ABlockedGenerationLeavesTheRequirementAGapAndQueuesTheModelsReason()
    {
        var (run, _) = await RunAsync();

        var requirement = run.Register.Single(requirement => requirement.Text == ReplaceValue);
        var generation = run.Generations.Single(generation => generation.RequirementId == requirement.Id);
        var decision = run.DecisionQueue.Single(decision => decision.RequirementId == requirement.Id);

        Assert.Empty(generation.Cases);
        Assert.Equal(BlockedReason, generation.BlockedReason);
        Assert.DoesNotContain(run.Cases, testCase => testCase.RequirementIds.Contains(requirement.Id));
        Assert.Equal(CoverageStatus.Gap, run.Matrix.Rows.Single(row => row.RequirementId == requirement.Id).Status);

        Assert.Equal(QueuedDecision.GenerationBlocked, decision.Reason);
        Assert.Equal(BlockedReason, decision.BlockedReason);
        Assert.Equal(ReplaceValue, decision.Item.Quote);
        Assert.Equal("4.3", decision.Item.Section);
        Assert.EndsWith("?", decision.Item.Question, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABlockedGenerationIsWrittenToTheDecisionsFileAndShownInTheMatrixPage()
    {
        var (run, _) = await RunAsync();
        using var directory = new ScratchDirectory();

        await RunArtifacts.WriteRunAsync(run, directory.Path, CancellationToken.None);

        var requirement = run.Register.Single(requirement => requirement.Text == ReplaceValue);
        var files = ArtifactInvariants.Read(directory.Path);
        var entry = files.Decisions.Single(decision =>
            decision.GetProperty("requirement_id").ValueKind == JsonValueKind.String
            && decision.GetProperty("requirement_id").GetString() == requirement.Id);

        Assert.Equal(QueuedDecision.GenerationBlocked, entry.GetProperty("reason").GetString());
        Assert.Equal(BlockedReason, entry.GetProperty("blocked_reason").GetString());
        Assert.Equal(ReplaceValue, entry.GetProperty("quote").GetString());
        Assert.Equal("4.3", entry.GetProperty("section").GetString());

        var html = await File.ReadAllTextAsync(Path.Combine(directory.Path, RunArtifacts.MatrixHtmlFile), CancellationToken.None);
        var dqId = entry.GetProperty("id").GetString()!;

        Assert.Contains(System.Net.WebUtility.HtmlEncode(BlockedReason), html, StringComparison.Ordinal);
        Assert.DoesNotContain("<absent>", html, StringComparison.Ordinal);
        Assert.Contains(
            $"<tr class=\"gap\"><td>{requirement.Id}</td>",
            html,
            StringComparison.Ordinal);
        Assert.Contains($"GAP<br>see {dqId}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDecisionQueueKeepsTheVerificationItemsAndAddsTheFlaggedAndBlockedOnes()
    {
        var (run, _) = await RunAsync();

        Assert.Equal(
            [
                QueuedDecision.QuoteFoundMoreThanOnce,
                QueuedDecision.ModelFlaggedNotTestable,
                QueuedDecision.ModelFlaggedNeedsHumanDecision,
                QueuedDecision.GenerationBlocked,
            ],
            run.DecisionQueue.Select(decision => decision.Reason));
        Assert.Equal(
            run.DecisionQueue.Count,
            run.DecisionQueue.Select(decision => decision.Item.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task EveryGeneratedCaseIsAProposalNamingOnlyTheRequirementItWasGeneratedFor()
    {
        var (run, _) = await RunAsync();

        var requirement = run.Register.Single(requirement => requirement.Text == AddValue);

        Assert.Equal(2, run.Cases.Count);
        Assert.All(run.Cases, testCase =>
        {
            Assert.Equal(ReviewStatus.Proposed, testCase.Status);
            Assert.Equal([requirement.Id], testCase.RequirementIds);
        });
        Assert.Equal(CoverageStatus.Covered, run.Matrix.Rows.Single(row => row.RequirementId == requirement.Id).Status);
    }

    [Fact]
    public async Task TheWrittenArtifactsOfARunHoldTheInvariants()
    {
        var (run, _) = await RunAsync();
        using var directory = new ScratchDirectory();

        await RunArtifacts.WriteRunAsync(run, directory.Path, CancellationToken.None);

        ArtifactInvariants.AssertHold(directory.Path, Corpus.Raw);
        Assert.Equal(
            [CoverageStatus.Gap, CoverageStatus.Gap, CoverageStatus.Covered, CoverageStatus.Gap],
            run.Matrix.Rows.Select(row => row.Status));
    }

    [Fact]
    public async Task AMalformedGenerationFailsTheWholeRunRatherThanKeepingPartOfIt()
    {
        var model = RoutingLlmClient.For(
            Extraction,
            quote => quote == ReplaceValue
                ? """{ "cases": [ { "title": "only a title" } ] }"""
                : GenerationAnswerJson.Cases(("positive", "a")));

        var exception = await Assert.ThrowsAsync<LlmResponseException>(() =>
            PipelineRun.ExecuteAsync(Corpus.Path, model, LlmClientFactory.DefaultModel, CancellationToken.None));

        Assert.Contains("REQ-rfc6902-", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<(PipelineRunResult Run, RoutingLlmClient Model)> RunAsync()
    {
        var model = RoutingLlmClient.For(
            Extraction,
            quote => quote switch
            {
                AddValue => GenerationAnswerJson.Cases(("positive", "Adding a value succeeds"), ("negative", "An add without a value is rejected")),
                ReplaceValue => GenerationAnswerJson.Blocked(BlockedReason),
                _ => throw new InvalidOperationException($"No generation was expected for '{quote}'."),
            });

        var run = await PipelineRun.ExecuteAsync(Corpus.Path, model, LlmClientFactory.DefaultModel, CancellationToken.None);

        return (run, model);
    }

    private static string ExtractionAnswer(params (string Modality, string Quote, string Testability, string? Note)[] entries) =>
        JsonSerializer.Serialize(entries.Select(entry => new Dictionary<string, object?>
        {
            ["modality"] = entry.Modality,
            ["quote"] = entry.Quote,
            ["testability"] = entry.Testability,
            ["testability_note"] = entry.Note,
        }));
}
