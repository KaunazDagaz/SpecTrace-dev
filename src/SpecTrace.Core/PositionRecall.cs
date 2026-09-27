namespace SpecTrace.Core;

public sealed record DocumentPart(int Number, int FirstLine, int LastLine, int Gold, int Matched)
{
    public double? Recall => Gold == 0 ? null : (double)Matched / Gold;
}

public enum ChunkingOutcome
{
    Needed,
    NotNeeded,
    Undetermined,
}

public static class PositionRecall
{
    public const int Parts = 3;

    public const int ChunkingThresholdPoints = 20;

    public static int LineCount(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var newlines = raw.Count(character => character == '\n');

        return raw.Length == 0 || raw[^1] == '\n' ? newlines : newlines + 1;
    }

    public static int LineOf(string raw, int offset)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, raw.Length);

        return 1 + raw.AsSpan(0, offset).Count('\n');
    }

    public static IReadOnlyList<DocumentPart> ByThird(string raw, IReadOnlyList<GoldRequirement> gold, MatchResult match)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(gold);
        ArgumentNullException.ThrowIfNull(match);

        var lines = LineCount(raw);
        var parts = new List<DocumentPart>();

        for (var number = 1; number <= Parts; number++)
        {
            var first = ((number - 1) * lines / Parts) + 1;
            var last = number * lines / Parts;
            var inPart = Enumerable.Range(0, gold.Count)
                .Where(index => LineOf(raw, gold[index].Span.Start) is var line && line >= first && line <= last)
                .ToList();

            parts.Add(new DocumentPart(
                number,
                first,
                last,
                inPart.Count,
                inPart.Count(index => match.ForGold(index) is not null)));
        }

        return parts;
    }

    public static ChunkingOutcome Chunking(DocumentPart first, DocumentPart last)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(last);

        if (first.Gold == 0 || last.Gold == 0)
        {
            return ChunkingOutcome.Undetermined;
        }

        var scaledDropInPoints = 100L * (((long)first.Matched * last.Gold) - ((long)last.Matched * first.Gold));

        return scaledDropInPoints >= (long)ChunkingThresholdPoints * first.Gold * last.Gold
            ? ChunkingOutcome.Needed
            : ChunkingOutcome.NotNeeded;
    }
}
