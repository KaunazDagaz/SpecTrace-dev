namespace SpecTrace.Core;

public enum HumanCoverageVerdict
{
    NotTestable,
    Deferred,
}

public sealed record HumanCoverageDecision
{
    public HumanCoverageDecision(string requirementId, HumanCoverageVerdict verdict)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirementId);

        if (!Enum.IsDefined(verdict))
        {
            throw new ArgumentOutOfRangeException(nameof(verdict), verdict, null);
        }

        RequirementId = requirementId;
        Verdict = verdict;
    }

    public string RequirementId { get; }

    public HumanCoverageVerdict Verdict { get; }
}
