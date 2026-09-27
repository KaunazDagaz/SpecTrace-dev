namespace SpecTrace.Core;

public sealed record ApproximateLocation
{
    internal ApproximateLocation(string normalizedQuote, int distance, IReadOnlyList<TextSpan> spans)
    {
        NormalizedQuote = normalizedQuote;
        Distance = distance;
        Spans = [.. spans];
    }

    public string NormalizedQuote { get; }

    public int Distance { get; }

    public IReadOnlyList<TextSpan> Spans { get; }

    public double Similarity => NormalizedQuote.Length == 0 ? 0 : 1 - ((double)Distance / NormalizedQuote.Length);

    public bool AtLeast(int similarityPercent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(similarityPercent, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(similarityPercent, 100);

        var length = NormalizedQuote.Length;

        return length > 0 && Spans.Count > 0 && (length - Distance) * 100L >= (long)similarityPercent * length;
    }
}

public static class ApproximateLocator
{
    public static ApproximateLocation Locate(NormalizedDocument document, string quote)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(quote);

        var pattern = TextNormalizer.Normalize(quote);
        var text = document.Normal;
        var length = pattern.Length;

        if (length == 0)
        {
            return new ApproximateLocation(pattern, 0, []);
        }

        var cost = new int[length + 1];
        var start = new int[length + 1];

        for (var row = 0; row <= length; row++)
        {
            cost[row] = row;
        }

        var best = length;
        var windows = new List<(int Start, int End)>();

        for (var column = 1; column <= text.Length; column++)
        {
            var diagonalCost = cost[0];
            var diagonalStart = start[0];

            cost[0] = 0;
            start[0] = column;

            for (var row = 1; row <= length; row++)
            {
                var leftCost = cost[row];
                var leftStart = start[row];

                var chosenCost = diagonalCost + (pattern[row - 1] == text[column - 1] ? 0 : 1);
                var chosenStart = diagonalStart;

                if (leftCost + 1 < chosenCost)
                {
                    chosenCost = leftCost + 1;
                    chosenStart = leftStart;
                }

                if (cost[row - 1] + 1 < chosenCost)
                {
                    chosenCost = cost[row - 1] + 1;
                    chosenStart = start[row - 1];
                }

                diagonalCost = leftCost;
                diagonalStart = leftStart;
                cost[row] = chosenCost;
                start[row] = chosenStart;
            }

            if (cost[length] < best)
            {
                best = cost[length];
                windows.Clear();
            }

            if (cost[length] == best && start[length] < column)
            {
                windows.Add((start[length], column));
            }
        }

        if (best == length)
        {
            return new ApproximateLocation(pattern, best, []);
        }

        var locations = new List<(int Start, int End)>();

        foreach (var window in windows)
        {
            if (locations.Count > 0 && window.Start < locations[^1].End)
            {
                continue;
            }

            locations.Add(window);
        }

        return new ApproximateLocation(
            pattern,
            best,
            [.. locations.Select(location => document.RawSpan(location.Start, location.End))]);
    }
}
