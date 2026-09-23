namespace SpecTrace.Core;

public sealed record TestCase
{
    public TestCase(
        string id,
        IEnumerable<string> requirementIds,
        string title,
        CaseType type,
        string precondition,
        string input,
        string expectedResult,
        ReviewStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(requirementIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedResult);

        var ids = requirementIds.ToArray();

        if (ids.Length == 0)
        {
            throw new ArgumentException(
                $"Test case '{id}' names no requirement. A test case must trace to at least one.",
                nameof(requirementIds));
        }

        if (ids.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                $"Test case '{id}' names a blank requirement ID.",
                nameof(requirementIds));
        }

        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            throw new ArgumentException(
                $"Test case '{id}' names the same requirement more than once.",
                nameof(requirementIds));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        Id = id;
        RequirementIds = Array.AsReadOnly(ids);
        Title = title;
        Type = type;
        Precondition = precondition;
        Input = input;
        ExpectedResult = expectedResult;
        Status = status;
    }

    public string Id { get; }

    public IReadOnlyList<string> RequirementIds { get; }

    public string Title { get; }

    public CaseType Type { get; }

    public string Precondition { get; }

    public string Input { get; }

    public string ExpectedResult { get; }

    public ReviewStatus Status { get; }

    public bool CountsTowardCoverage => Status != ReviewStatus.Rejected;
}
