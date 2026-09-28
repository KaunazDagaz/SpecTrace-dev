namespace SpecTrace.Core.Tests;

public sealed class ReviewedRunTests
{
    private const string RunId = "doc-000000000000";

    private static readonly DateTimeOffset At = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);

    private static readonly QueueEntry[] NoQueue = [];

    [Fact]
    public void WithAnEmptyLogEveryCaseStaysProposedAndTheMatrixEqualsThePipelinesMatrix()
    {
        Requirement[] register = [Requirement("aaaaaa", 100), Requirement("bbbbbb", 200)];
        TestCase[] cases = [Case("aaaaaa", 1)];

        var reviewed = ReviewedRun.Build(RunId, register, cases, NoQueue, []);

        Assert.All(reviewed.Cases, reviewedCase => Assert.Same(reviewedCase.Original, reviewedCase.Current));
        Assert.All(reviewed.Cases, reviewedCase => Assert.Null(reviewedCase.Decision));
        Assert.Equal(
            TraceabilityMatrix.Build(register, cases, []).Rows,
            reviewed.Matrix.Rows,
            RowComparer.Instance);
    }

    [Fact]
    public void RejectingARequirementsOnlyCaseTurnsItIntoAGap()
    {
        var reviewed = ReviewedRun.Build(
            RunId,
            [Requirement("aaaaaa", 100)],
            [Case("aaaaaa", 1)],
            NoQueue,
            [Reject("TC-aaaaaa-01")]);

        var row = Assert.Single(reviewed.Matrix.Rows);
        Assert.Equal(CoverageStatus.Gap, row.Status);
        Assert.Empty(row.TestCaseIds);
        Assert.Equal(ReviewStatus.Rejected, Assert.Single(reviewed.Cases).Current.Status);
    }

    [Fact]
    public void RejectingOneOfTwoCasesLeavesTheRequirementCoveredByTheOther()
    {
        var reviewed = ReviewedRun.Build(
            RunId,
            [Requirement("aaaaaa", 100)],
            [Case("aaaaaa", 1), Case("aaaaaa", 2)],
            NoQueue,
            [Reject("TC-aaaaaa-01")]);

        var row = Assert.Single(reviewed.Matrix.Rows);
        Assert.Equal(CoverageStatus.Covered, row.Status);
        Assert.Equal(["TC-aaaaaa-02"], row.TestCaseIds);
    }

    [Fact]
    public void AnEditCarriesTheNewTextAndKeepsTheOriginalUntouched()
    {
        var original = Case("aaaaaa", 1);
        var edit = new CaseEdit("A sharper title", CaseType.Negative, "A document.", "A bad patch.", "It is rejected.");

        var reviewed = ReviewedRun.Build(
            RunId,
            [Requirement("aaaaaa", 100)],
            [original],
            NoQueue,
            [new ReviewRecord(RunId, "TC-aaaaaa-01", CaseDecision.Edit, edit, "Reviewer", At)]);

        var reviewedCase = Assert.Single(reviewed.Cases);
        Assert.Same(original, reviewedCase.Original);
        Assert.Equal("A case", reviewedCase.Original.Title);
        Assert.Equal(ReviewStatus.Proposed, reviewedCase.Original.Status);
        Assert.Equal("A sharper title", reviewedCase.Current.Title);
        Assert.Equal(CaseType.Negative, reviewedCase.Current.Type);
        Assert.Equal("It is rejected.", reviewedCase.Current.ExpectedResult);
        Assert.Equal(ReviewStatus.Edited, reviewedCase.Current.Status);
        Assert.Equal(original.RequirementIds, reviewedCase.Current.RequirementIds);
        Assert.Equal(CoverageStatus.Covered, Assert.Single(reviewed.Matrix.Rows).Status);
    }

    [Fact]
    public void TheLatestDecisionOnACaseWinsAndEarlierOnesStayInTheLog()
    {
        var register = new[] { Requirement("aaaaaa", 100) };
        var cases = new[] { Case("aaaaaa", 1) };

        var rejectedThenAccepted = ReviewedRun.Build(RunId, register, cases, NoQueue, [Reject("TC-aaaaaa-01"), Accept("TC-aaaaaa-01")]);
        var acceptedThenRejected = ReviewedRun.Build(RunId, register, cases, NoQueue, [Accept("TC-aaaaaa-01"), Reject("TC-aaaaaa-01")]);

        Assert.Equal(CoverageStatus.Covered, Assert.Single(rejectedThenAccepted.Matrix.Rows).Status);
        Assert.Equal(CaseDecision.Accept, Assert.Single(rejectedThenAccepted.Cases).Decision!.Decision);
        Assert.Equal(CoverageStatus.Gap, Assert.Single(acceptedThenRejected.Matrix.Rows).Status);
    }

    [Theory]
    [InlineData(QueueDecision.NotTestable, CoverageStatus.NotTestable)]
    [InlineData(QueueDecision.Defer, CoverageStatus.DeferredByHuman)]
    public void AHumanNotTestableOrDeferDecisionTakesABlockedRequirementOutOfTheGaps(
        QueueDecision decision,
        CoverageStatus expected)
    {
        var reviewed = ReviewedRun.Build(
            RunId,
            [Requirement("aaaaaa", 100)],
            [],
            [new QueueEntry("DQ-doc-aaaaaa", "REQ-doc-aaaaaa")],
            [Resolve("DQ-doc-aaaaaa", "REQ-doc-aaaaaa", decision)]);

        Assert.Equal(expected, Assert.Single(reviewed.Matrix.Rows).Status);
        Assert.Equal("REQ-doc-aaaaaa", Assert.Single(reviewed.HumanDecisions).RequirementId);
    }

    [Theory]
    [InlineData(Testability.NotTestable)]
    [InlineData(Testability.NeedsHumanDecision)]
    public void ARequirementTheModelFlaggedIsAGapUntilAHumanDecides(Testability flagged)
    {
        var reviewed = ReviewedRun.Build(
            RunId,
            [Requirement("aaaaaa", 100, flagged)],
            [],
            [new QueueEntry("DQ-doc-aaaaaa", "REQ-doc-aaaaaa")],
            []);

        Assert.Equal(CoverageStatus.Gap, Assert.Single(reviewed.Matrix.Rows).Status);
        Assert.Empty(reviewed.HumanDecisions);
    }

    [Fact]
    public void ResolvingAnItemAsTestableIsLoggedButLeavesTheRequirementAGapBecauseNoCaseIsGenerated()
    {
        var reviewed = ReviewedRun.Build(
            RunId,
            [Requirement("aaaaaa", 100, Testability.NotTestable)],
            [],
            [new QueueEntry("DQ-doc-aaaaaa", "REQ-doc-aaaaaa")],
            [Resolve("DQ-doc-aaaaaa", "REQ-doc-aaaaaa", QueueDecision.Testable)]);

        Assert.Equal(CoverageStatus.Gap, Assert.Single(reviewed.Matrix.Rows).Status);
        Assert.Equal(QueueDecision.Testable, Assert.Single(reviewed.Items).Decision!.Decision);
        Assert.Empty(reviewed.HumanDecisions);
        Assert.Empty(reviewed.Cases);
    }

    [Fact]
    public void ALaterTestableDecisionReturnsARequirementMarkedNotTestableToTheGaps()
    {
        var reviewed = ReviewedRun.Build(
            RunId,
            [Requirement("aaaaaa", 100)],
            [],
            [new QueueEntry("DQ-doc-aaaaaa", "REQ-doc-aaaaaa")],
            [
                Resolve("DQ-doc-aaaaaa", "REQ-doc-aaaaaa", QueueDecision.NotTestable),
                Resolve("DQ-doc-aaaaaa", "REQ-doc-aaaaaa", QueueDecision.Testable),
            ]);

        Assert.Equal(CoverageStatus.Gap, Assert.Single(reviewed.Matrix.Rows).Status);
    }

    [Fact]
    public void ADecisionOnAnItemWithNoRequirementInTheRegisterIsLoggedAndChangesNoRow()
    {
        var register = new[] { Requirement("aaaaaa", 100) };

        var reviewed = ReviewedRun.Build(
            RunId,
            register,
            [],
            [new QueueEntry("DQ-doc-ambig0", null)],
            [Resolve("DQ-doc-ambig0", null, QueueDecision.NotTestable)]);

        Assert.Equal(QueueDecision.NotTestable, Assert.Single(reviewed.Items).Decision!.Decision);
        Assert.Empty(reviewed.HumanDecisions);
        Assert.Empty(reviewed.OrphanDecisions);
        Assert.Equal(register.Select(requirement => requirement.Id), reviewed.Matrix.Rows.Select(row => row.RequirementId));
        Assert.Equal(CoverageStatus.Gap, Assert.Single(reviewed.Matrix.Rows).Status);
    }

    [Fact]
    public void DecisionsWhoseTargetIsNotInTheRunAreListedAsOrphansWithTheirLineAndNeverApplied()
    {
        var reviewed = ReviewedRun.Build(
            RunId,
            [Requirement("aaaaaa", 100)],
            [Case("aaaaaa", 1)],
            [new QueueEntry("DQ-doc-aaaaaa", "REQ-doc-aaaaaa")],
            [
                Accept("TC-aaaaaa-01"),
                Reject("TC-gone00-01"),
                new ReviewRecord("other-111111111111", "TC-aaaaaa-01", CaseDecision.Reject, null, "Reviewer", At),
                Resolve("DQ-doc-gone00", "REQ-doc-gone00", QueueDecision.Defer),
                Resolve("DQ-doc-aaaaaa", "REQ-doc-bbbbbb", QueueDecision.Defer),
            ]);

        Assert.Equal([2, 3, 4, 5], reviewed.OrphanDecisions.Select(orphan => orphan.Line));
        Assert.Contains("TC-gone00-01", reviewed.OrphanDecisions[0].Reason, StringComparison.Ordinal);
        Assert.Contains("other-111111111111", reviewed.OrphanDecisions[1].Reason, StringComparison.Ordinal);
        Assert.Contains("DQ-doc-gone00", reviewed.OrphanDecisions[2].Reason, StringComparison.Ordinal);
        Assert.Contains("REQ-doc-bbbbbb", reviewed.OrphanDecisions[3].Reason, StringComparison.Ordinal);

        Assert.Equal(ReviewStatus.Accepted, Assert.Single(reviewed.Cases).Current.Status);
        Assert.Null(Assert.Single(reviewed.Items).Decision);
        Assert.Equal(CoverageStatus.Covered, Assert.Single(reviewed.Matrix.Rows).Status);
    }

    [Fact]
    public void ADecisionOnAnItemWhoseRequirementLeftTheRegisterIsAnOrphan()
    {
        var reviewed = ReviewedRun.Build(
            RunId,
            [Requirement("aaaaaa", 100)],
            [],
            [new QueueEntry("DQ-doc-bbbbbb", "REQ-doc-bbbbbb")],
            [Resolve("DQ-doc-bbbbbb", "REQ-doc-bbbbbb", QueueDecision.NotTestable)]);

        var orphan = Assert.Single(reviewed.OrphanDecisions);
        Assert.Contains("not in the register", orphan.Reason, StringComparison.Ordinal);
        Assert.Empty(reviewed.HumanDecisions);
    }

    [Fact]
    public void StatusFollowsI4ThroughTheLogOverRandomRunsAndDecisions()
    {
        const int Seed = 13;
        const int Combinations = 500;
        var random = new Random(Seed);

        for (var combination = 0; combination < Combinations; combination++)
        {
            var register = Enumerable.Range(0, random.Next(1, 7))
                .Select(index => Requirement($"{index:x6}", index * 100, (Testability)random.Next(0, 3)))
                .ToList();
            var cases = register
                .SelectMany(requirement => Enumerable.Range(1, random.Next(0, 3))
                    .Select(ordinal => Case(requirement.Id["REQ-doc-".Length..], ordinal)))
                .ToList();
            var queue = register
                .Where(requirement => random.Next(0, 2) == 0)
                .Select(requirement => new QueueEntry($"DQ-doc-{requirement.Id["REQ-doc-".Length..]}", requirement.Id))
                .ToList();
            var log = Enumerable.Range(0, random.Next(0, 12))
                .Select(_ => RandomDecision(random, cases, queue))
                .OfType<LoggedDecision>()
                .ToList();

            var reviewed = ReviewedRun.Build(RunId, register, cases, queue, log);
            var context = $"seed {Seed}, combination {combination}";

            foreach (var row in reviewed.Matrix.Rows)
            {
                var nonRejected = cases.Count(testCase =>
                    testCase.RequirementIds.Contains(row.RequirementId)
                    && log.OfType<ReviewRecord>().LastOrDefault(record => record.TestCaseId == testCase.Id)?.Decision
                        != CaseDecision.Reject);
                var itemDecision = log.OfType<QueueResolution>()
                    .LastOrDefault(resolution => resolution.RequirementId == row.RequirementId)?.Decision;
                var humanOutOfGaps = itemDecision is QueueDecision.NotTestable or QueueDecision.Defer;

                Assert.True(
                    (row.Status == CoverageStatus.Gap) == (nonRejected == 0 && !humanOutOfGaps),
                    $"{context}: {row.RequirementId} is {row.Status} with {nonRejected} non-rejected cases "
                    + $"and latest item decision {itemDecision?.ToString() ?? "none"}.");
            }
        }
    }

    [Fact]
    public void ARunWithADuplicateCaseOrItemIdIsRefused()
    {
        Assert.Throws<ArgumentException>(() => ReviewedRun.Build(
            RunId, [Requirement("aaaaaa", 100)], [Case("aaaaaa", 1), Case("aaaaaa", 1)], NoQueue, []));
        Assert.Throws<ArgumentException>(() => ReviewedRun.Build(
            RunId,
            [Requirement("aaaaaa", 100)],
            [],
            [new QueueEntry("DQ-doc-aaaaaa", null), new QueueEntry("DQ-doc-aaaaaa", null)],
            []));
    }

    private static LoggedDecision? RandomDecision(Random random, List<TestCase> cases, List<QueueEntry> queue)
    {
        if (random.Next(0, 2) == 0 && cases.Count > 0)
        {
            var testCase = cases[random.Next(cases.Count)];
            var decision = (CaseDecision)random.Next(0, 3);
            var edit = decision == CaseDecision.Edit
                ? new CaseEdit("Edited", CaseType.Boundary, string.Empty, "An input.", "A result.")
                : null;

            return new ReviewRecord(RunId, testCase.Id, decision, edit, "Reviewer", At);
        }

        if (queue.Count == 0)
        {
            return null;
        }

        var entry = queue[random.Next(queue.Count)];

        return Resolve(entry.ItemId, entry.RequirementId, (QueueDecision)random.Next(0, 3));
    }

    private static ReviewRecord Accept(string caseId) =>
        new(RunId, caseId, CaseDecision.Accept, null, "Reviewer", At);

    private static ReviewRecord Reject(string caseId) =>
        new(RunId, caseId, CaseDecision.Reject, null, "Reviewer", At);

    private static QueueResolution Resolve(string itemId, string? requirementId, QueueDecision decision) =>
        new(RunId, itemId, requirementId, decision, "Reviewer", At);

    private static Requirement Requirement(string hash, int start, Testability testability = Testability.Testable) =>
        new(
            $"REQ-doc-{hash}",
            "doc",
            "4.1",
            Modality.Must,
            $"Requirement {hash} MUST hold.",
            new TextSpan(start, start + 20),
            testability,
            testabilityNote: null);

    private static TestCase Case(string requirementHash, int ordinal) =>
        new(
            $"TC-{requirementHash}-{ordinal:D2}",
            [$"REQ-doc-{requirementHash}"],
            "A case",
            CaseType.Positive,
            string.Empty,
            "An input.",
            "An observable result.",
            ReviewStatus.Proposed);

    private sealed class RowComparer : IEqualityComparer<MatrixRow>
    {
        public static readonly RowComparer Instance = new();

        public bool Equals(MatrixRow? x, MatrixRow? y) =>
            x is not null && y is not null
            && x.RequirementId == y.RequirementId
            && x.Status == y.Status
            && x.TestCaseIds.SequenceEqual(y.TestCaseIds);

        public int GetHashCode(MatrixRow obj) => obj.RequirementId.GetHashCode(StringComparison.Ordinal);
    }
}
