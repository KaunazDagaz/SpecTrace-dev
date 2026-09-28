namespace SpecTrace.Core.Tests;

public sealed class ReviewRecordTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 28, 9, 0, 0, TimeSpan.Zero);

    private static readonly CaseEdit Edit = new("Title", CaseType.Positive, string.Empty, string.Empty, "A result.");

    [Fact]
    public void AnEditWithoutNewTextCannotBeConstructed()
    {
        Assert.Throws<ArgumentException>(() =>
            new ReviewRecord("run", "TC-a-01", CaseDecision.Edit, null, "Reviewer", At));
    }

    [Theory]
    [InlineData(CaseDecision.Accept)]
    [InlineData(CaseDecision.Reject)]
    public void AnAcceptOrRejectCarryingNewTextCannotBeConstructed(CaseDecision decision)
    {
        Assert.Throws<ArgumentException>(() =>
            new ReviewRecord("run", "TC-a-01", decision, Edit, "Reviewer", At));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" Padded")]
    [InlineData("Line\nbreak")]
    public void ADecisionWithoutAUsableAuthorNameCannotBeConstructed(string author)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new ReviewRecord("run", "TC-a-01", CaseDecision.Accept, null, author, At));
        Assert.ThrowsAny<ArgumentException>(() =>
            new QueueResolution("run", "DQ-a", null, QueueDecision.Defer, author, At));
    }

    [Fact]
    public void AnAuthorNameLongerThanTheLimitIsRefused()
    {
        var author = new string('a', LoggedDecision.MaxAuthorLength + 1);

        Assert.Throws<ArgumentException>(() =>
            new ReviewRecord("run", "TC-a-01", CaseDecision.Accept, null, author, At));
    }

    [Fact]
    public void AnEditedCaseNeedsATitleAndAnExpectedResultLikeAnyTestCase()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CaseEdit(" ", CaseType.Positive, string.Empty, string.Empty, "A result."));
        Assert.ThrowsAny<ArgumentException>(() => new CaseEdit("Title", CaseType.Positive, string.Empty, string.Empty, " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaseEdit("Title", (CaseType)9, string.Empty, string.Empty, "A result."));
        Assert.Throws<ArgumentException>(() => new CaseEdit(
            new string('t', CaseEdit.MaxFieldLength + 1), CaseType.Positive, string.Empty, string.Empty, "A result."));
    }

    [Fact]
    public void AnEditIdenticalToTheOriginalChangesNothing()
    {
        var original = new TestCase("TC-a-01", ["REQ-doc-a"], "Title", CaseType.Positive, string.Empty, string.Empty, "A result.", ReviewStatus.Proposed);

        Assert.True(Edit.ChangesNothingIn(original));
        Assert.False(new CaseEdit("Title", CaseType.Negative, string.Empty, string.Empty, "A result.").ChangesNothingIn(original));
    }
}
