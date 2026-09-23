namespace SpecTrace.Core;

public sealed class TraceabilityMatrix
{
    private TraceabilityMatrix(IReadOnlyList<MatrixRow> rows, IReadOnlyList<OrphanCase> orphans)
    {
        Rows = rows;
        Orphans = orphans;
    }

    public IReadOnlyList<MatrixRow> Rows { get; }

    public IReadOnlyList<OrphanCase> Orphans { get; }

    public static TraceabilityMatrix Build(
        IReadOnlyList<Requirement> register,
        IReadOnlyList<TestCase> cases,
        IReadOnlyList<HumanCoverageDecision> humanDecisions)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(humanDecisions);

        var coveringCases = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var requirement in register)
        {
            if (!coveringCases.TryAdd(requirement.Id, []))
            {
                throw new ArgumentException(
                    $"Requirement ID '{requirement.Id}' appears more than once in the register.",
                    nameof(register));
            }
        }

        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        var orphans = new List<OrphanCase>();

        foreach (var testCase in cases)
        {
            if (!caseIds.Add(testCase.Id))
            {
                throw new ArgumentException(
                    $"Test case ID '{testCase.Id}' appears more than once in the case set.",
                    nameof(cases));
            }

            var missing = testCase.RequirementIds
                .Where(requirementId => !coveringCases.ContainsKey(requirementId))
                .ToArray();

            if (missing.Length > 0)
            {
                orphans.Add(new OrphanCase(testCase.Id, testCase.RequirementIds, Array.AsReadOnly(missing)));
            }

            if (!testCase.CountsTowardCoverage)
            {
                continue;
            }

            foreach (var requirementId in testCase.RequirementIds)
            {
                if (coveringCases.TryGetValue(requirementId, out var covering))
                {
                    covering.Add(testCase.Id);
                }
            }
        }

        var verdicts = new Dictionary<string, HumanCoverageVerdict>(StringComparer.Ordinal);

        foreach (var decision in humanDecisions)
        {
            verdicts[decision.RequirementId] = decision.Verdict;
        }

        var rows = register
            .OrderBy(requirement => requirement.Span.Start)
            .ThenBy(requirement => requirement.Span.End)
            .ThenBy(requirement => requirement.Id, StringComparer.Ordinal)
            .Select(requirement =>
            {
                var testCaseIds = coveringCases[requirement.Id].Order(StringComparer.Ordinal).ToArray();

                return new MatrixRow(
                    requirement.Id,
                    requirement.Modality,
                    requirement.Section,
                    Array.AsReadOnly(testCaseIds),
                    StatusFor(testCaseIds.Length, verdicts.TryGetValue(requirement.Id, out var verdict) ? verdict : null));
            })
            .ToArray();

        var orderedOrphans = orphans
            .OrderBy(orphan => orphan.TestCaseId, StringComparer.Ordinal)
            .ToArray();

        return new TraceabilityMatrix(Array.AsReadOnly(rows), Array.AsReadOnly(orderedOrphans));
    }

    private static CoverageStatus StatusFor(int coveringCaseCount, HumanCoverageVerdict? humanVerdict)
    {
        if (coveringCaseCount > 0)
        {
            return CoverageStatus.Covered;
        }

        return humanVerdict switch
        {
            null => CoverageStatus.Gap,
            HumanCoverageVerdict.NotTestable => CoverageStatus.NotTestable,
            HumanCoverageVerdict.Deferred => CoverageStatus.DeferredByHuman,
            _ => throw new ArgumentOutOfRangeException(nameof(humanVerdict), humanVerdict, null),
        };
    }
}
