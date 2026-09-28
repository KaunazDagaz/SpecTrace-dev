namespace SpecTrace.Core;

public sealed record CaseEdit
{
    public const int MaxFieldLength = 10_000;

    public CaseEdit(string title, CaseType type, string precondition, string input, string expectedResult)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(precondition);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedResult);

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        foreach (var (name, value) in new[]
        {
            (nameof(title), title),
            (nameof(precondition), precondition),
            (nameof(input), input),
            (nameof(expectedResult), expectedResult),
        })
        {
            if (value.Length > MaxFieldLength)
            {
                throw new ArgumentException(
                    $"An edited case's {name} holds {value.Length} characters; at most {MaxFieldLength} are allowed.",
                    name);
            }
        }

        Title = title;
        Type = type;
        Precondition = precondition;
        Input = input;
        ExpectedResult = expectedResult;
    }

    public string Title { get; }

    public CaseType Type { get; }

    public string Precondition { get; }

    public string Input { get; }

    public string ExpectedResult { get; }

    public bool ChangesNothingIn(TestCase original)
    {
        ArgumentNullException.ThrowIfNull(original);

        return Title == original.Title
            && Type == original.Type
            && Precondition == original.Precondition
            && Input == original.Input
            && ExpectedResult == original.ExpectedResult;
    }

    public TestCase ApplyTo(TestCase original)
    {
        ArgumentNullException.ThrowIfNull(original);

        return new TestCase(
            original.Id,
            original.RequirementIds,
            Title,
            Type,
            Precondition,
            Input,
            ExpectedResult,
            ReviewStatus.Edited);
    }
}
