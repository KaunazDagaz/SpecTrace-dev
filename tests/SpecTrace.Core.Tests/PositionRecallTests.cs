namespace SpecTrace.Core.Tests;

public sealed class PositionRecallTests
{
    [Fact]
    public void LinesAreCountedWithOrWithoutATrailingNewline()
    {
        Assert.Equal(3, PositionRecall.LineCount("a\nb\nc\n"));
        Assert.Equal(3, PositionRecall.LineCount("a\nb\nc"));
        Assert.Equal(0, PositionRecall.LineCount(string.Empty));
        Assert.Equal(1, PositionRecall.LineOf("a\nb\nc", 0));
        Assert.Equal(2, PositionRecall.LineOf("a\nb\nc", 2));
        Assert.Equal(3, PositionRecall.LineOf("a\nb\nc", 4));
    }

    [Fact]
    public void EachGoldRequirementFallsInTheThirdHoldingTheFirstLineOfItsSpan()
    {
        var raw = string.Concat(Enumerable.Range(1, 9).Select(line => $"line {line}\n"));
        GoldRequirement[] gold =
        [
            Fixtures.Gold(new TextSpan(Offset(raw, 1), Offset(raw, 1) + 6), Modality.Must, 1),
            Fixtures.Gold(new TextSpan(Offset(raw, 3), Offset(raw, 5)), Modality.Must, 2),
            Fixtures.Gold(new TextSpan(Offset(raw, 4), Offset(raw, 4) + 6), Modality.Must, 3),
            Fixtures.Gold(new TextSpan(Offset(raw, 9), Offset(raw, 9) + 6), Modality.Must, 4),
        ];
        Prediction[] predictions = [new([gold[0].Span], Modality.Must), new([gold[3].Span], Modality.Must)];

        var parts = PositionRecall.ByThird(raw, gold, SpanMatching.Match(predictions, gold, 50));

        Assert.Equal(
            [(1, 1, 3, 2, 1), (2, 4, 6, 1, 0), (3, 7, 9, 1, 1)],
            parts.Select(part => (part.Number, part.FirstLine, part.LastLine, part.Gold, part.Matched)));
        Assert.Equal([0.5, 0.0, 1.0], parts.Select(part => part.Recall));
    }

    [Fact]
    public void AThirdWithNoGoldRequirementHasNoRecallRatherThanZero()
    {
        var raw = string.Concat(Enumerable.Range(1, 9).Select(line => $"line {line}\n"));
        GoldRequirement[] gold = [Fixtures.Gold(new TextSpan(Offset(raw, 2), Offset(raw, 2) + 6), Modality.Must)];

        var parts = PositionRecall.ByThird(raw, gold, SpanMatching.Match([], gold, 50));

        Assert.Equal([0.0, null, null], parts.Select(part => part.Recall));
    }

    [Theory]
    [InlineData(5, 5, 4, 5, ChunkingOutcome.Needed)]
    [InlineData(5, 5, 5, 6, ChunkingOutcome.NotNeeded)]
    [InlineData(10, 13, 3, 6, ChunkingOutcome.Needed)]
    [InlineData(10, 13, 4, 6, ChunkingOutcome.NotNeeded)]
    [InlineData(3, 5, 5, 5, ChunkingOutcome.NotNeeded)]
    [InlineData(5, 5, 0, 0, ChunkingOutcome.Undetermined)]
    [InlineData(0, 0, 3, 5, ChunkingOutcome.Undetermined)]
    public void ChunkingIsNeededOnlyWhenRecallInTheLastThirdIsAtLeastTwentyPointsBelowTheFirst(
        int firstMatched,
        int firstGold,
        int lastMatched,
        int lastGold,
        ChunkingOutcome expected)
    {
        var first = new DocumentPart(1, 1, 10, firstGold, firstMatched);
        var last = new DocumentPart(3, 21, 30, lastGold, lastMatched);

        Assert.Equal(expected, PositionRecall.Chunking(first, last));
    }

    private static int Offset(string raw, int line)
    {
        var offset = 0;

        for (var current = 1; current < line; current++)
        {
            offset = raw.IndexOf('\n', offset) + 1;
        }

        return offset;
    }
}
