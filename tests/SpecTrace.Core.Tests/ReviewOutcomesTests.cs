namespace SpecTrace.Core.Tests;

public sealed class ReviewOutcomesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CasesAreCountedByTheirLatestDecisionAndEarlierDecisionsCountAsSuperseded()
    {
        var edit = new CaseEdit("Title", CaseType.Positive, string.Empty, string.Empty, "A result.");

        var outcomes = ReviewOutcomes.Of(
        [
            Case("TC-a-01", CaseDecision.Reject, author: "Ann"),
            Case("TC-a-01", CaseDecision.Accept, author: "Ann"),
            Case("TC-a-02", CaseDecision.Accept, author: "Ann"),
            new ReviewRecord("run", "TC-a-03", CaseDecision.Edit, edit, "Bo", At),
            Case("TC-a-04", CaseDecision.Reject, author: "Bo"),
            Case("TC-a-05", CaseDecision.Accept, author: "Bo"),
            Case("TC-a-05", CaseDecision.Reject, author: "Bo"),
            new QueueResolution("run", "DQ-a", "REQ-doc-a", QueueDecision.NotTestable, "Ann", At),
            new QueueResolution("run", "DQ-b", null, QueueDecision.Testable, "Ann", At),
            new QueueResolution("run", "DQ-c", "REQ-doc-c", QueueDecision.Testable, "Ann", At),
            new QueueResolution("run", "DQ-c", "REQ-doc-c", QueueDecision.Defer, "Ann", At),
        ]);

        Assert.Equal(11, outcomes.Lines);
        Assert.Equal(3, outcomes.Superseded);
        Assert.Equal(2, outcomes.CasesAccepted);
        Assert.Equal(1, outcomes.CasesEdited);
        Assert.Equal(2, outcomes.CasesRejected);
        Assert.Equal(5, outcomes.CasesDecided);
        Assert.Equal(1, outcomes.ItemsTestable);
        Assert.Equal(1, outcomes.ItemsNotTestable);
        Assert.Equal(1, outcomes.ItemsDeferred);
        Assert.Equal(["Ann", "Bo"], outcomes.Authors);
        Assert.Equal(["run"], outcomes.RunIds);
    }

    [Fact]
    public void AnEmptyLogHasNoOutcomes()
    {
        var outcomes = ReviewOutcomes.Of([]);

        Assert.Equal(0, outcomes.Lines);
        Assert.Equal(0, outcomes.CasesDecided);
        Assert.Equal(0, outcomes.ItemsDecided);
        Assert.Empty(outcomes.Authors);
    }

    private static ReviewRecord Case(string caseId, CaseDecision decision, string author) =>
        new("run", caseId, decision, null, author, At);
}
