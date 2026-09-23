namespace SpecTrace.Core.Tests;

public sealed class TestCaseTests
{
    [Fact]
    public void ATestCaseThatNamesNoRequirementCannotBeConstructed()
    {
        Assert.Throws<ArgumentException>(() => Case(requirementIds: []));
    }

    [Fact]
    public void ATestCaseThatNamesABlankRequirementIdCannotBeConstructed()
    {
        Assert.Throws<ArgumentException>(() => Case(requirementIds: ["REQ-rfc6902-a3f1c2", " "]));
    }

    [Fact]
    public void ATestCaseThatNamesTheSameRequirementTwiceCannotBeConstructed()
    {
        Assert.Throws<ArgumentException>(() => Case(requirementIds: ["REQ-rfc6902-a3f1c2", "REQ-rfc6902-a3f1c2"]));
    }

    [Fact]
    public void EmptyingTheListATestCaseWasBuiltFromLeavesItsRequirementIdsIntact()
    {
        var ids = new List<string> { "REQ-rfc6902-a3f1c2" };
        var testCase = Case(requirementIds: ids);

        ids.Clear();

        Assert.Equal(["REQ-rfc6902-a3f1c2"], testCase.RequirementIds);
    }

    [Fact]
    public void ATestCasesRequirementIdsCannotBeEmptiedThroughTheListItExposes()
    {
        var testCase = Case(requirementIds: ["REQ-rfc6902-a3f1c2"]);
        var exposed = Assert.IsAssignableFrom<IList<string>>(testCase.RequirementIds);

        Assert.Throws<NotSupportedException>(() => exposed.Clear());
        Assert.Single(testCase.RequirementIds);
    }

    [Fact]
    public void ATestCaseWithNoTitleOrNoExpectedResultCannotBeConstructed()
    {
        Assert.Throws<ArgumentException>(() => Case(title: " "));
        Assert.Throws<ArgumentException>(() => Case(expectedResult: ""));
    }

    [Fact]
    public void OnlyARejectedCaseStopsCountingTowardCoverage()
    {
        Assert.True(Case(status: ReviewStatus.Proposed).CountsTowardCoverage);
        Assert.True(Case(status: ReviewStatus.Accepted).CountsTowardCoverage);
        Assert.True(Case(status: ReviewStatus.Edited).CountsTowardCoverage);
        Assert.False(Case(status: ReviewStatus.Rejected).CountsTowardCoverage);
    }

    private static TestCase Case(
        IEnumerable<string>? requirementIds = null,
        string title = "Adding a member to an object succeeds",
        string expectedResult = "The member is present in the result.",
        ReviewStatus status = ReviewStatus.Proposed) =>
        new(
            "TC-a3f1c2-01",
            requirementIds ?? ["REQ-rfc6902-a3f1c2"],
            title,
            CaseType.Positive,
            "A target JSON object.",
            "An add operation.",
            expectedResult,
            status);
}
