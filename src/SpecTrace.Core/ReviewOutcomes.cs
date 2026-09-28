namespace SpecTrace.Core;

public sealed record ReviewOutcomes(
    int Lines,
    int Superseded,
    int CasesAccepted,
    int CasesEdited,
    int CasesRejected,
    int ItemsTestable,
    int ItemsNotTestable,
    int ItemsDeferred,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> RunIds)
{
    public int CasesDecided => CasesAccepted + CasesEdited + CasesRejected;

    public int ItemsDecided => ItemsTestable + ItemsNotTestable + ItemsDeferred;

    public static ReviewOutcomes Of(IReadOnlyList<LoggedDecision> log)
    {
        ArgumentNullException.ThrowIfNull(log);

        var latestCase = new Dictionary<(string RunId, string CaseId), CaseDecision>();
        var latestItem = new Dictionary<(string RunId, string ItemId), QueueDecision>();

        foreach (var decision in log)
        {
            switch (decision)
            {
                case ReviewRecord record:
                    latestCase[(record.RunId, record.TestCaseId)] = record.Decision;
                    break;
                case QueueResolution resolution:
                    latestItem[(resolution.RunId, resolution.ItemId)] = resolution.Decision;
                    break;
                default:
                    throw new ArgumentException("The log holds an unknown kind of decision.", nameof(log));
            }
        }

        return new ReviewOutcomes(
            log.Count,
            log.Count - latestCase.Count - latestItem.Count,
            latestCase.Values.Count(decision => decision == CaseDecision.Accept),
            latestCase.Values.Count(decision => decision == CaseDecision.Edit),
            latestCase.Values.Count(decision => decision == CaseDecision.Reject),
            latestItem.Values.Count(decision => decision == QueueDecision.Testable),
            latestItem.Values.Count(decision => decision == QueueDecision.NotTestable),
            latestItem.Values.Count(decision => decision == QueueDecision.Defer),
            Distinct(log.Select(decision => decision.Author)),
            Distinct(log.Select(decision => decision.RunId)));
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList().AsReadOnly();
}
