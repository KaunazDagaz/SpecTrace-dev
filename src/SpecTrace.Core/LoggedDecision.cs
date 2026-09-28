namespace SpecTrace.Core;

public abstract record LoggedDecision
{
    public const int MaxAuthorLength = 100;

    protected LoggedDecision(string runId, string author, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        if (author != author.Trim())
        {
            throw new ArgumentException("An author name must not start or end with whitespace.", nameof(author));
        }

        if (author.Length > MaxAuthorLength)
        {
            throw new ArgumentException(
                $"An author name holds {author.Length} characters; at most {MaxAuthorLength} are allowed.",
                nameof(author));
        }

        if (author.Any(char.IsControl))
        {
            throw new ArgumentException("An author name must not contain control characters.", nameof(author));
        }

        RunId = runId;
        Author = author;
        At = at;
    }

    public string RunId { get; }

    public string Author { get; }

    public DateTimeOffset At { get; }

    public abstract string TargetId { get; }
}

public sealed record ReviewRecord : LoggedDecision
{
    public ReviewRecord(
        string runId,
        string testCaseId,
        CaseDecision decision,
        CaseEdit? edit,
        string author,
        DateTimeOffset at)
        : base(runId, author, at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testCaseId);

        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, null);
        }

        if (decision == CaseDecision.Edit && edit is null)
        {
            throw new ArgumentException($"An edit of '{testCaseId}' must carry the new text.", nameof(edit));
        }

        if (decision != CaseDecision.Edit && edit is not null)
        {
            throw new ArgumentException(
                $"Only an edit carries new text; '{testCaseId}' was decided '{decision}'.",
                nameof(edit));
        }

        TestCaseId = testCaseId;
        Decision = decision;
        Edit = edit;
    }

    public string TestCaseId { get; }

    public CaseDecision Decision { get; }

    public CaseEdit? Edit { get; }

    public override string TargetId => TestCaseId;
}

public sealed record QueueResolution : LoggedDecision
{
    public QueueResolution(
        string runId,
        string itemId,
        string? requirementId,
        QueueDecision decision,
        string author,
        DateTimeOffset at)
        : base(runId, author, at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        if (requirementId is not null && string.IsNullOrWhiteSpace(requirementId))
        {
            throw new ArgumentException($"Item '{itemId}' names a blank requirement ID.", nameof(requirementId));
        }

        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, null);
        }

        ItemId = itemId;
        RequirementId = requirementId;
        Decision = decision;
    }

    public string ItemId { get; }

    public string? RequirementId { get; }

    public QueueDecision Decision { get; }

    public override string TargetId => ItemId;
}
