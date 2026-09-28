namespace SpecTrace.Core;

public sealed record QueueEntry
{
    public QueueEntry(string itemId, string? requirementId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        if (requirementId is not null && string.IsNullOrWhiteSpace(requirementId))
        {
            throw new ArgumentException($"Item '{itemId}' names a blank requirement ID.", nameof(requirementId));
        }

        ItemId = itemId;
        RequirementId = requirementId;
    }

    public string ItemId { get; }

    public string? RequirementId { get; }
}

public sealed record ReviewedCase(TestCase Original, TestCase Current, ReviewRecord? Decision);

public sealed record ReviewedItem(QueueEntry Entry, QueueResolution? Decision);

public sealed record OrphanDecision(int Line, LoggedDecision Decision, string Reason);

public sealed class ReviewedRun
{
    private ReviewedRun(
        IReadOnlyList<ReviewedCase> cases,
        IReadOnlyList<ReviewedItem> items,
        IReadOnlyList<HumanCoverageDecision> humanDecisions,
        TraceabilityMatrix matrix,
        IReadOnlyList<OrphanDecision> orphanDecisions)
    {
        Cases = cases;
        Items = items;
        HumanDecisions = humanDecisions;
        Matrix = matrix;
        OrphanDecisions = orphanDecisions;
    }

    public IReadOnlyList<ReviewedCase> Cases { get; }

    public IReadOnlyList<ReviewedItem> Items { get; }

    public IReadOnlyList<HumanCoverageDecision> HumanDecisions { get; }

    public TraceabilityMatrix Matrix { get; }

    public IReadOnlyList<OrphanDecision> OrphanDecisions { get; }

    public static ReviewedRun Build(
        string runId,
        IReadOnlyList<Requirement> register,
        IReadOnlyList<TestCase> cases,
        IReadOnlyList<QueueEntry> queue,
        IReadOnlyList<LoggedDecision> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(log);

        var registerIds = register.Select(requirement => requirement.Id).ToHashSet(StringComparer.Ordinal);
        var casesById = new Dictionary<string, TestCase>(StringComparer.Ordinal);

        foreach (var testCase in cases)
        {
            if (!casesById.TryAdd(testCase.Id, testCase))
            {
                throw new ArgumentException($"Test case ID '{testCase.Id}' appears more than once.", nameof(cases));
            }
        }

        var itemsById = new Dictionary<string, QueueEntry>(StringComparer.Ordinal);

        foreach (var entry in queue)
        {
            if (!itemsById.TryAdd(entry.ItemId, entry))
            {
                throw new ArgumentException($"Decision-queue item '{entry.ItemId}' appears more than once.", nameof(queue));
            }
        }

        var latestCase = new Dictionary<string, ReviewRecord>(StringComparer.Ordinal);
        var latestItem = new Dictionary<string, QueueResolution>(StringComparer.Ordinal);
        var orphans = new List<OrphanDecision>();

        for (var index = 0; index < log.Count; index++)
        {
            var decision = log[index];
            var line = index + 1;
            var reason = OrphanReason(decision, runId, casesById, itemsById, registerIds);

            if (reason is not null)
            {
                orphans.Add(new OrphanDecision(line, decision, reason));
                continue;
            }

            switch (decision)
            {
                case ReviewRecord record:
                    latestCase[record.TestCaseId] = record;
                    break;
                case QueueResolution resolution:
                    latestItem[resolution.ItemId] = resolution;
                    break;
                default:
                    throw new ArgumentException($"Line {line} holds an unknown kind of decision.", nameof(log));
            }
        }

        var reviewedCases = cases
            .Select(original =>
            {
                var record = latestCase.GetValueOrDefault(original.Id);

                return new ReviewedCase(original, Current(original, record), record);
            })
            .ToArray();

        var reviewedItems = queue
            .Select(entry => new ReviewedItem(entry, latestItem.GetValueOrDefault(entry.ItemId)))
            .ToArray();

        var humanDecisions = new List<HumanCoverageDecision>();

        foreach (var item in reviewedItems)
        {
            if (item.Entry.RequirementId is { } requirementId
                && item.Decision is { } resolution
                && VerdictFor(resolution.Decision) is { } verdict)
            {
                humanDecisions.Add(new HumanCoverageDecision(requirementId, verdict));
            }
        }

        var matrix = TraceabilityMatrix.Build(
            register,
            reviewedCases.Select(reviewed => reviewed.Current).ToArray(),
            humanDecisions);

        return new ReviewedRun(
            Array.AsReadOnly(reviewedCases),
            Array.AsReadOnly(reviewedItems),
            humanDecisions.AsReadOnly(),
            matrix,
            orphans.AsReadOnly());
    }

    private static string? OrphanReason(
        LoggedDecision decision,
        string runId,
        Dictionary<string, TestCase> casesById,
        Dictionary<string, QueueEntry> itemsById,
        HashSet<string> registerIds)
    {
        if (decision.RunId != runId)
        {
            return $"logged for run {decision.RunId}, not for run {runId}";
        }

        return decision switch
        {
            ReviewRecord record => casesById.ContainsKey(record.TestCaseId)
                ? null
                : $"no test case {record.TestCaseId} in this run",
            QueueResolution resolution => ItemOrphanReason(resolution, itemsById, registerIds),
            _ => null,
        };
    }

    private static string? ItemOrphanReason(
        QueueResolution resolution,
        Dictionary<string, QueueEntry> itemsById,
        HashSet<string> registerIds)
    {
        if (!itemsById.TryGetValue(resolution.ItemId, out var entry))
        {
            return $"no decision-queue item {resolution.ItemId} in this run";
        }

        if (resolution.RequirementId != entry.RequirementId)
        {
            return $"names requirement {resolution.RequirementId ?? "none"}, but item {entry.ItemId} "
                + $"names {entry.RequirementId ?? "none"}";
        }

        if (entry.RequirementId is not null && !registerIds.Contains(entry.RequirementId))
        {
            return $"requirement {entry.RequirementId} is not in the register";
        }

        return null;
    }

    private static TestCase Current(TestCase original, ReviewRecord? record) => record?.Decision switch
    {
        null => original,
        CaseDecision.Accept => WithStatus(original, ReviewStatus.Accepted),
        CaseDecision.Reject => WithStatus(original, ReviewStatus.Rejected),
        CaseDecision.Edit => record.Edit!.ApplyTo(original),
        _ => throw new ArgumentOutOfRangeException(nameof(record), record.Decision, null),
    };

    private static TestCase WithStatus(TestCase original, ReviewStatus status) => new(
        original.Id,
        original.RequirementIds,
        original.Title,
        original.Type,
        original.Precondition,
        original.Input,
        original.ExpectedResult,
        status);

    private static HumanCoverageVerdict? VerdictFor(QueueDecision decision) => decision switch
    {
        QueueDecision.Testable => null,
        QueueDecision.NotTestable => HumanCoverageVerdict.NotTestable,
        QueueDecision.Defer => HumanCoverageVerdict.Deferred,
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, null),
    };
}
