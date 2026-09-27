using System.Text.RegularExpressions;

namespace SpecTrace.Pipeline;

public static partial class ClaimParser
{
    private const int MaximumLabelWords = 4;

    private static readonly Dictionary<char, char> Closers = new()
    {
        ['"'] = '"',
        ['“'] = '”',
        ['„'] = '“',
        ['«'] = '»',
        ['\''] = '\'',
        ['‘'] = '’',
        ['`'] = '`',
    };

    private static readonly HashSet<char> ClosedOnlyAtTheEnd = ['\'', '`'];

    private static readonly string[] Emphasis = ["**", "__", "*", "_"];

    public static IReadOnlyList<ClaimedQuote> Parse(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var lines = answer.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var headings = Headings(lines);
        var fields = QuoteFields(lines);
        var itemLevel = ItemLevel(headings, fields);

        if (itemLevel is null && fields.Count == 0)
        {
            throw new UnparseableAnswerException(
                "The answer has neither a quote field nor a repeated heading or numbered entry, so it cannot be "
                + "split into claimed requirements.");
        }

        var claims = new List<ClaimedQuote>();
        var assigned = new HashSet<int>();

        foreach (var item in headings.Where(heading => heading.Level == itemLevel))
        {
            var end = SectionEnd(headings, item, lines.Length);
            var inItem = fields.Where(field => field.Line >= item.Line && field.Line < end).ToList();

            assigned.UnionWith(inItem.Select(field => field.Line));

            if (inItem.Count == 0)
            {
                inItem = Blockquotes(lines, item.Line + 1, end);
            }

            if (inItem.Count == 0)
            {
                claims.Add(new ClaimedQuote(0, item.Text, item.Line + 1, lines[item.Line], Quote: null));
            }

            claims.AddRange(inItem.Select(field => new ClaimedQuote(0, item.Text, field.Line + 1, field.Source, field.Quote)));
        }

        claims.AddRange(fields
            .Where(field => !assigned.Contains(field.Line))
            .Select(field => new ClaimedQuote(0, string.Empty, field.Line + 1, field.Source, field.Quote)));

        return claims
            .OrderBy(claim => claim.AnswerLine)
            .Select((claim, index) => claim with { Ordinal = index + 1 })
            .ToList();
    }

    public static string? Unwrap(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var text = value.Trim();

        while (true)
        {
            var wrapper = Emphasis.FirstOrDefault(marker =>
                text.Length > 2 * marker.Length
                && text.StartsWith(marker, StringComparison.Ordinal)
                && text.EndsWith(marker, StringComparison.Ordinal));

            if (wrapper is null)
            {
                break;
            }

            text = text[wrapper.Length..^wrapper.Length].Trim();
        }

        var leading = Emphasis.FirstOrDefault(marker =>
            text.Length > marker.Length
            && text.StartsWith(marker, StringComparison.Ordinal)
            && Closers.ContainsKey(text[marker.Length]));

        if (leading is not null)
        {
            text = text[leading.Length..];
        }

        if (text.Length > 1 && Closers.TryGetValue(text[0], out var closer))
        {
            if (ClosedOnlyAtTheEnd.Contains(text[0]))
            {
                if (text[^1] == closer)
                {
                    text = text[1..^1];
                }
            }
            else
            {
                var close = text.LastIndexOf(closer);

                text = close > 0 ? text[1..close] : text[1..];
            }
        }

        return text.Trim().Length == 0 ? null : text;
    }

    private static int SectionEnd(List<Heading> headings, Heading item, int lineCount) =>
        headings.FirstOrDefault(heading => heading.Line > item.Line && heading.Closes(item))?.Line ?? lineCount;

    private static string? ItemLevel(List<Heading> headings, List<QuoteField> fields)
    {
        var levelsAboveQuotes = fields
            .Select(field => headings.LastOrDefault(heading => heading.Line <= field.Line))
            .OfType<Heading>()
            .Select(heading => heading.Level)
            .ToList();

        if (fields.Count > 0 && levelsAboveQuotes.Count == 0)
        {
            return null;
        }

        var candidates = fields.Count > 0 ? levelsAboveQuotes : headings.Select(heading => heading.Level).ToList();

        var chosen = candidates
            .GroupBy(level => level, StringComparer.Ordinal)
            .Select(group => (Level: group.Key, Count: group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Level, StringComparer.Ordinal)
            .FirstOrDefault();

        return chosen.Level is null || (fields.Count == 0 && chosen.Count < 2) ? null : chosen.Level;
    }

    private static List<Heading> Headings(string[] lines)
    {
        var headings = new List<Heading>();

        for (var i = 0; i < lines.Length; i++)
        {
            var atx = AtxHeadingPattern().Match(lines[i]);

            if (atx.Success)
            {
                headings.Add(new Heading(i, $"h{atx.Groups["hashes"].Length}", lines[i].Trim()));
            }
            else if (NumberedEntryPattern().IsMatch(lines[i]))
            {
                headings.Add(new Heading(i, Heading.NumberedLevel, lines[i].Trim()));
            }
        }

        return headings;
    }

    private static List<QuoteField> QuoteFields(string[] lines)
    {
        var fields = new List<QuoteField>();

        for (var i = 0; i < lines.Length; i++)
        {
            var content = ListMarkerPattern().Replace(lines[i], string.Empty, 1);
            var numbered = NumberedEntryPattern().Match(content);

            if (numbered.Success)
            {
                content = numbered.Groups["text"].Value;
            }

            var field = FieldPattern().Match(content);

            if (!field.Success || !IsQuoteLabel(field.Groups["label"].Value))
            {
                continue;
            }

            var start = i;
            var value = field.Groups["value"].Value;
            var source = lines[i];

            if (Unwrap(value) is null && FollowingQuote(lines, i + 1) is { } following)
            {
                value = following.Value;
                source = string.Join('\n', lines[i..(following.EndLine + 1)]);
                i = following.EndLine;
            }

            var quotes = Quotes(value);

            if (quotes.Count == 0)
            {
                fields.Add(new QuoteField(start, source, Quote: null));
            }

            fields.AddRange(quotes.Select(quote => new QuoteField(start, source, quote)));
        }

        return fields;
    }

    public static IReadOnlyList<string> Quotes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return QuoteBoundaryPattern()
            .Split(value.Trim())
            .Select(Unwrap)
            .OfType<string>()
            .ToList();
    }

    private static (string Value, int EndLine)? FollowingQuote(string[] lines, int start)
    {
        var next = start;

        while (next < lines.Length && lines[next].Trim().Length == 0)
        {
            next++;
        }

        if (next >= lines.Length)
        {
            return null;
        }

        if (BlockquotePattern().IsMatch(lines[next]))
        {
            var end = next;

            while (end + 1 < lines.Length && BlockquotePattern().IsMatch(lines[end + 1]))
            {
                end++;
            }

            return (BlockquoteText(lines, next, end), end);
        }

        var trimmed = lines[next].TrimStart();

        return trimmed.Length > 0 && Closers.ContainsKey(trimmed[0]) ? (trimmed, next) : null;
    }

    private static List<QuoteField> Blockquotes(string[] lines, int start, int end)
    {
        var blocks = new List<QuoteField>();

        for (var i = start; i < end; i++)
        {
            if (!BlockquotePattern().IsMatch(lines[i]))
            {
                continue;
            }

            var last = i;

            while (last + 1 < end && BlockquotePattern().IsMatch(lines[last + 1]))
            {
                last++;
            }

            blocks.Add(new QuoteField(i, string.Join('\n', lines[i..(last + 1)]), Unwrap(BlockquoteText(lines, i, last))));
            i = last;
        }

        return blocks;
    }

    private static string BlockquoteText(string[] lines, int first, int last) =>
        string.Join('\n', lines[first..(last + 1)].Select(line => BlockquotePattern().Replace(line, string.Empty, 1)));

    private static bool IsQuoteLabel(string label)
    {
        var words = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length is > 0 and <= MaximumLabelWords
            && words.Any(word =>
                word.StartsWith("quot", StringComparison.OrdinalIgnoreCase)
                || word.StartsWith("sentence", StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex("^ {0,3}(?<hashes>#{1,6})[ \t]+\\S", RegexOptions.CultureInvariant)]
    private static partial Regex AtxHeadingPattern();

    [GeneratedRegex("^(?:\\*\\*|__)?(?<number>[0-9]{1,3})[.)](?:\\*\\*|__)?[ \t]+(?<text>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedEntryPattern();

    [GeneratedRegex("^[ \t]*[-*+][ \t]+", RegexOptions.CultureInvariant)]
    private static partial Regex ListMarkerPattern();

    [GeneratedRegex("^(?:\\*\\*|__|\\*|_)?(?<label>[A-Za-z][A-Za-z ()-]{0,40}?)(?:\\*\\*|__|\\*|_)?[ \t]*:(?:\\*\\*|__|\\*|_)?[ \t]*(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldPattern();

    [GeneratedRegex("^[ \t]*>[ \t]?", RegexOptions.CultureInvariant)]
    private static partial Regex BlockquotePattern();

    [GeneratedRegex("(?<=[.!?:][\"\u201D])[ \t]+and[ \t]+(?=[\"\u201C])", RegexOptions.CultureInvariant)]
    private static partial Regex QuoteBoundaryPattern();

    private sealed record Heading(int Line, string Level, string Text)
    {
        public const string NumberedLevel = "numbered";

        public bool Closes(Heading item) =>
            Level == item.Level
            || (Level != NumberedLevel && item.Level == NumberedLevel)
            || (Level != NumberedLevel && item.Level != NumberedLevel && string.CompareOrdinal(Level, item.Level) < 0);
    }

    private sealed record QuoteField(int Line, string Source, string? Quote);
}

public sealed record ClaimedQuote(int Ordinal, string Item, int AnswerLine, string Source, string? Quote);

public sealed class UnparseableAnswerException : Exception
{
    public UnparseableAnswerException(string message)
        : base(message)
    {
    }
}
