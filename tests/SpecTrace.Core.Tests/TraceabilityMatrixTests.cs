using System.Text;

namespace SpecTrace.Core.Tests;

public sealed class TraceabilityMatrixTests
{
    private static readonly HumanCoverageDecision[] NoHumanDecisions = [];

    [Fact]
    public void EveryRequirementInTheRegisterAppearsInTheMatrixExactlyOnce()
    {
        Requirement[] register = [Requirement("aaaaaa", 100), Requirement("bbbbbb", 200), Requirement("cccccc", 300)];

        var matrix = TraceabilityMatrix.Build(register, [Case("aaaaaa", 1)], NoHumanDecisions);

        Assert.Equal(register.Select(requirement => requirement.Id), matrix.Rows.Select(row => row.RequirementId));
    }

    [Fact]
    public void RowsFollowPositionInTheDocumentWhateverOrderTheRegisterArrivesIn()
    {
        Requirement[] register = [Requirement("cccccc", 300), Requirement("aaaaaa", 100), Requirement("bbbbbb", 200)];

        var matrix = TraceabilityMatrix.Build(register, [], NoHumanDecisions);

        Assert.Equal(
            ["REQ-doc-aaaaaa", "REQ-doc-bbbbbb", "REQ-doc-cccccc"],
            matrix.Rows.Select(row => row.RequirementId));
    }

    [Fact]
    public void ARequirementWithANonRejectedCaseIsCoveredAndOneWithoutIsAGap()
    {
        var matrix = TraceabilityMatrix.Build(
            [Requirement("aaaaaa", 100), Requirement("bbbbbb", 200)],
            [Case("aaaaaa", 1)],
            NoHumanDecisions);

        Assert.Equal([CoverageStatus.Covered, CoverageStatus.Gap], matrix.Rows.Select(row => row.Status));
        Assert.Equal(["TC-aaaaaa-01"], matrix.Rows[0].TestCaseIds);
        Assert.Empty(matrix.Rows[1].TestCaseIds);
    }

    [Fact]
    public void ARequirementWithMixedAcceptedAndRejectedCasesIsCoveredByTheAcceptedOnesOnly()
    {
        var matrix = TraceabilityMatrix.Build(
            [Requirement("aaaaaa", 100)],
            [
                Case("aaaaaa", 1, ReviewStatus.Rejected),
                Case("aaaaaa", 2, ReviewStatus.Accepted),
                Case("aaaaaa", 3, ReviewStatus.Edited),
            ],
            NoHumanDecisions);

        var row = Assert.Single(matrix.Rows);
        Assert.Equal(CoverageStatus.Covered, row.Status);
        Assert.Equal(["TC-aaaaaa-02", "TC-aaaaaa-03"], row.TestCaseIds);
    }

    [Fact]
    public void ARequirementWhoseEveryCaseWasRejectedIsAGap()
    {
        var matrix = TraceabilityMatrix.Build(
            [Requirement("aaaaaa", 100)],
            [Case("aaaaaa", 1, ReviewStatus.Rejected), Case("aaaaaa", 2, ReviewStatus.Rejected)],
            NoHumanDecisions);

        Assert.Equal(CoverageStatus.Gap, Assert.Single(matrix.Rows).Status);
    }

    [Theory]
    [InlineData(Testability.NotTestable)]
    [InlineData(Testability.NeedsHumanDecision)]
    public void ARequirementTheModelFlaggedStaysAGapUntilAHumanDecides(Testability flagged)
    {
        var matrix = TraceabilityMatrix.Build(
            [Requirement("aaaaaa", 100, flagged)],
            [],
            NoHumanDecisions);

        Assert.Equal(CoverageStatus.Gap, Assert.Single(matrix.Rows).Status);
    }

    [Theory]
    [InlineData(HumanCoverageVerdict.NotTestable, CoverageStatus.NotTestable)]
    [InlineData(HumanCoverageVerdict.Deferred, CoverageStatus.DeferredByHuman)]
    public void OnlyALoggedHumanDecisionTakesARequirementWithoutCasesOutOfTheGaps(
        HumanCoverageVerdict verdict,
        CoverageStatus expected)
    {
        var matrix = TraceabilityMatrix.Build(
            [Requirement("aaaaaa", 100, Testability.NotTestable)],
            [],
            [new HumanCoverageDecision("REQ-doc-aaaaaa", verdict)]);

        Assert.Equal(expected, Assert.Single(matrix.Rows).Status);
    }

    [Fact]
    public void ALaterHumanDecisionOnTheSameRequirementSupersedesAnEarlierOne()
    {
        var matrix = TraceabilityMatrix.Build(
            [Requirement("aaaaaa", 100)],
            [],
            [
                new HumanCoverageDecision("REQ-doc-aaaaaa", HumanCoverageVerdict.NotTestable),
                new HumanCoverageDecision("REQ-doc-aaaaaa", HumanCoverageVerdict.Deferred),
            ]);

        Assert.Equal(CoverageStatus.DeferredByHuman, Assert.Single(matrix.Rows).Status);
    }

    [Fact]
    public void ACaseWhoseRequirementWasRemovedFromTheRegisterIsListedAsAnOrphanNotDropped()
    {
        var cases = new[] { Case("aaaaaa", 1), Case("bbbbbb", 1), Case("bbbbbb", 2) };

        var withBoth = TraceabilityMatrix.Build(
            [Requirement("aaaaaa", 100), Requirement("bbbbbb", 200)], cases, NoHumanDecisions);
        var withoutB = TraceabilityMatrix.Build(
            [Requirement("aaaaaa", 100)], cases, NoHumanDecisions);

        Assert.Empty(withBoth.Orphans);

        Assert.Equal(["TC-bbbbbb-01", "TC-bbbbbb-02"], withoutB.Orphans.Select(orphan => orphan.TestCaseId));
        Assert.All(withoutB.Orphans, orphan => Assert.Equal(["REQ-doc-bbbbbb"], orphan.MissingRequirementIds));
        Assert.Equal(["REQ-doc-aaaaaa"], withoutB.Rows.Select(row => row.RequirementId));
    }

    [Fact]
    public void ACaseNamingOneKnownAndOneMissingRequirementCoversTheKnownOneAndIsListedAsAnOrphan()
    {
        var shared = new TestCase(
            "TC-shared-01",
            ["REQ-doc-aaaaaa", "REQ-doc-gone00"],
            "A case naming two requirements",
            CaseType.Positive,
            string.Empty,
            "An input.",
            "An observable result.",
            ReviewStatus.Proposed);

        var matrix = TraceabilityMatrix.Build([Requirement("aaaaaa", 100)], [shared], NoHumanDecisions);

        Assert.Equal(CoverageStatus.Covered, Assert.Single(matrix.Rows).Status);
        var orphan = Assert.Single(matrix.Orphans);
        Assert.Equal(["REQ-doc-aaaaaa", "REQ-doc-gone00"], orphan.RequirementIds);
        Assert.Equal(["REQ-doc-gone00"], orphan.MissingRequirementIds);
    }

    [Fact]
    public void ARegisterWithADuplicateRequirementIdIsRefused()
    {
        Assert.Throws<ArgumentException>(() => TraceabilityMatrix.Build(
            [Requirement("aaaaaa", 100), Requirement("aaaaaa", 100)], [], NoHumanDecisions));
    }

    [Fact]
    public void ACaseSetWithADuplicateCaseIdIsRefused()
    {
        Assert.Throws<ArgumentException>(() => TraceabilityMatrix.Build(
            [Requirement("aaaaaa", 100)], [Case("aaaaaa", 1), Case("aaaaaa", 1)], NoHumanDecisions));
    }

    [Fact]
    public void StatusIsGapIfAndOnlyIfThereIsNoNonRejectedCaseAndNoHumanDecisionOverRandomCombinations()
    {
        const int Seed = 6902;
        const int Combinations = 1000;
        var random = new Random(Seed);

        for (var combination = 0; combination < Combinations; combination++)
        {
            var (register, cases, decisions) = RandomCombination(random);
            var matrix = TraceabilityMatrix.Build(register, cases, decisions);
            var context = $"seed {Seed}, combination {combination}";

            Assert.Equal(register.Count, matrix.Rows.Count);
            Assert.Equal(
                register.Select(requirement => requirement.Id).Order(StringComparer.Ordinal),
                matrix.Rows.Select(row => row.RequirementId).Order(StringComparer.Ordinal));

            foreach (var row in matrix.Rows)
            {
                var nonRejected = cases
                    .Where(testCase => testCase.Status != ReviewStatus.Rejected
                        && testCase.RequirementIds.Contains(row.RequirementId))
                    .Select(testCase => testCase.Id)
                    .Order(StringComparer.Ordinal)
                    .ToList();
                var lastDecision = decisions.LastOrDefault(decision => decision.RequirementId == row.RequirementId);

                Assert.True(
                    (row.Status == CoverageStatus.Gap) == (nonRejected.Count == 0 && lastDecision is null),
                    $"{context}: {row.RequirementId} is {row.Status} with {nonRejected.Count} non-rejected "
                    + $"cases and human decision {lastDecision?.Verdict.ToString() ?? "none"}.");

                Assert.Equal(ExpectedStatus(nonRejected.Count, lastDecision), row.Status);
                Assert.Equal(nonRejected, row.TestCaseIds);
            }

            var registerIds = register.Select(requirement => requirement.Id).ToHashSet(StringComparer.Ordinal);
            var expectedOrphans = cases
                .Where(testCase => testCase.RequirementIds.Any(id => !registerIds.Contains(id)))
                .Select(testCase => testCase.Id)
                .Order(StringComparer.Ordinal);

            Assert.Equal(expectedOrphans, matrix.Orphans.Select(orphan => orphan.TestCaseId));

            var shuffled = TraceabilityMatrix.Build(
                register.OrderBy(_ => random.Next()).ToList(),
                cases.OrderBy(_ => random.Next()).ToList(),
                decisions);

            Assert.Equal(Describe(matrix), Describe(shuffled));
        }
    }

    private static CoverageStatus ExpectedStatus(int nonRejectedCases, HumanCoverageDecision? lastDecision) =>
        nonRejectedCases > 0
            ? CoverageStatus.Covered
            : lastDecision?.Verdict switch
            {
                null => CoverageStatus.Gap,
                HumanCoverageVerdict.NotTestable => CoverageStatus.NotTestable,
                HumanCoverageVerdict.Deferred => CoverageStatus.DeferredByHuman,
                _ => throw new InvalidOperationException(),
            };

    private static (List<Requirement> Register, List<TestCase> Cases, List<HumanCoverageDecision> Decisions)
        RandomCombination(Random random)
    {
        var register = Enumerable.Range(0, random.Next(0, 9))
            .Select(index => Requirement(
                $"{index:x6}",
                random.Next(0, 5000),
                (Testability)random.Next(0, 3)))
            .ToList();

        var namable = register.Select(requirement => requirement.Id)
            .Concat(Enumerable.Range(0, 3).Select(index => $"REQ-doc-gone{index:x2}"))
            .ToList();

        var cases = Enumerable.Range(0, random.Next(0, 13))
            .Select(index => new TestCase(
                $"TC-random-{index:D2}",
                namable.OrderBy(_ => random.Next()).Take(random.Next(1, 4)).ToList(),
                "A case",
                (CaseType)random.Next(0, 3),
                string.Empty,
                "An input.",
                "An observable result.",
                (ReviewStatus)random.Next(0, 4)))
            .ToList();

        var decisions = Enumerable.Range(0, random.Next(0, register.Count + 2))
            .Select(_ => new HumanCoverageDecision(
                namable[random.Next(namable.Count)],
                (HumanCoverageVerdict)random.Next(0, 2)))
            .ToList();

        return (register, cases, decisions);
    }

    private static string Describe(TraceabilityMatrix matrix)
    {
        var description = new StringBuilder();

        foreach (var row in matrix.Rows)
        {
            description.Append($"{row.RequirementId}|{row.Status}|{string.Join(",", row.TestCaseIds)}\n");
        }

        foreach (var orphan in matrix.Orphans)
        {
            description.Append($"orphan {orphan.TestCaseId}|{string.Join(",", orphan.MissingRequirementIds)}\n");
        }

        return description.ToString();
    }

    private static Requirement Requirement(
        string hash,
        int start,
        Testability testability = Testability.Testable) =>
        new(
            $"REQ-doc-{hash}",
            "doc",
            "4.1",
            Modality.Must,
            $"Requirement {hash} MUST hold.",
            new TextSpan(start, start + 20),
            testability,
            testabilityNote: null);

    private static TestCase Case(string requirementHash, int ordinal, ReviewStatus status = ReviewStatus.Proposed) =>
        new(
            $"TC-{requirementHash}-{ordinal:D2}",
            [$"REQ-doc-{requirementHash}"],
            "A case",
            CaseType.Positive,
            string.Empty,
            "An input.",
            "An observable result.",
            status);
}
