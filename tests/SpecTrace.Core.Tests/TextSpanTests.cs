namespace SpecTrace.Core.Tests;

public sealed class TextSpanTests
{
    [Fact]
    public void LengthIsTheDistanceFromStartToEnd()
    {
        Assert.Equal(15, new TextSpan(10, 25).Length);
    }

    [Fact]
    public void AnEmptySpanHasZeroLength()
    {
        Assert.Equal(0, new TextSpan(42, 42).Length);
    }

    [Fact]
    public void PartiallyOverlappingSpansOverlap()
    {
        AssertOverlapIsSymmetric(new TextSpan(0, 10), new TextSpan(5, 15), expected: true);
    }

    [Fact]
    public void AContainedSpanOverlapsTheSpanContainingIt()
    {
        AssertOverlapIsSymmetric(new TextSpan(0, 100), new TextSpan(10, 20), expected: true);
    }

    [Fact]
    public void SpansThatOnlyTouchAtTheirBoundaryDoNotOverlap()
    {
        AssertOverlapIsSymmetric(new TextSpan(0, 10), new TextSpan(10, 20), expected: false);
    }

    [Fact]
    public void DisjointSpansDoNotOverlap()
    {
        AssertOverlapIsSymmetric(new TextSpan(0, 5), new TextSpan(10, 15), expected: false);
    }

    [Fact]
    public void TwoSpansWithTheSameBoundsAreEqual()
    {
        Assert.Equal(new TextSpan(8421, 8535), new TextSpan(8421, 8535));
    }

    private static void AssertOverlapIsSymmetric(TextSpan left, TextSpan right, bool expected)
    {
        Assert.Equal(expected, left.Overlaps(right));
        Assert.Equal(expected, right.Overlaps(left));
    }
}
