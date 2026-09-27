using System.Text.RegularExpressions;

namespace SpecTrace.Core;

public static partial class KeywordSentences
{
    public static IReadOnlyList<KeywordSentence> Find(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var lines = SourceLine.Split(raw);
        var furniture = PageFurniture(raw, lines);
        var sections = SectionIndex.Build(raw);
        var masked = Mask(raw, lines, furniture);
        var found = new List<KeywordSentence>();

        foreach (var (first, last) in Paragraphs(raw, lines, furniture))
        {
            var start = lines[first].Start;
            var paragraph = NormalizedDocument.Create(masked[start..lines[last].End]);

            foreach (var (from, to) in Sentences(paragraph.Normal))
            {
                var text = paragraph.Normal[from..to];
                var keywords = Keyword().Matches(text).Select(match => match.Value).ToList();

                if (keywords.Count == 0)
                {
                    continue;
                }

                var span = new TextSpan(start + paragraph.Map[from], start + paragraph.Map[to - 1] + 1);
                var firstLine = SourceLine.IndexOf(lines, span.Start);
                var lastLine = SourceLine.IndexOf(lines, span.End - 1);

                var sourceLines = Enumerable.Range(firstLine, lastLine - firstLine + 1)
                    .Where(index => !furniture[index] && !lines[index].IsBlank(raw))
                    .Select(index => lines[index].Text(raw).Trim())
                    .ToList();

                found.Add(new KeywordSentence(
                    found.Count + 1,
                    sections.SectionFor(span),
                    firstLine + 1,
                    lastLine + 1,
                    keywords,
                    text,
                    sourceLines,
                    span));
            }
        }

        return found;
    }

    private static bool[] PageFurniture(string raw, IReadOnlyList<SourceLine> lines)
    {
        var furniture = new bool[lines.Count];

        for (var index = 0; index < lines.Count; index++)
        {
            var text = lines[index].Text(raw);

            if (!text.Contains('\f', StringComparison.Ordinal))
            {
                continue;
            }

            furniture[index] = true;

            if (Nearest(raw, lines, index, -1) is { } footer && PageFooter().IsMatch(lines[footer].Text(raw)))
            {
                furniture[footer] = true;
            }

            if (text.Replace('\f', ' ').Trim().Length == 0
                && Nearest(raw, lines, index, +1) is { } header
                && PageHeader().IsMatch(lines[header].Text(raw)))
            {
                furniture[header] = true;
            }
        }

        return furniture;
    }

    private static int? Nearest(string raw, IReadOnlyList<SourceLine> lines, int from, int step)
    {
        for (var index = from + step; index >= 0 && index < lines.Count; index += step)
        {
            if (!lines[index].IsBlank(raw))
            {
                return index;
            }
        }

        return null;
    }

    private static string Mask(string raw, IReadOnlyList<SourceLine> lines, bool[] furniture)
    {
        var characters = raw.ToCharArray();

        for (var index = 0; index < lines.Count; index++)
        {
            if (furniture[index])
            {
                Array.Fill(characters, ' ', lines[index].Start, lines[index].End - lines[index].Start);
            }
        }

        return new string(characters);
    }

    private static IEnumerable<(int First, int Last)> Paragraphs(
        string raw,
        IReadOnlyList<SourceLine> lines,
        bool[] furniture)
    {
        int? first = null;
        var last = -1;
        var gapHasBlank = false;
        var gapHasPageBreak = false;

        for (var index = 0; index < lines.Count; index++)
        {
            if (furniture[index])
            {
                gapHasPageBreak = true;
                continue;
            }

            if (lines[index].IsBlank(raw))
            {
                gapHasBlank = true;
                continue;
            }

            if (first is null)
            {
                first = index;
            }
            else if (gapHasBlank && !gapHasPageBreak)
            {
                yield return (first.Value, last);
                first = index;
            }

            last = index;
            gapHasBlank = false;
            gapHasPageBreak = false;
        }

        if (first is not null)
        {
            yield return (first.Value, last);
        }
    }

    private static IEnumerable<(int From, int To)> Sentences(string paragraph)
    {
        var from = 0;

        for (var index = 0; index < paragraph.Length; index++)
        {
            if (paragraph[index] is '.' or '!' or '?'
                && (index + 1 == paragraph.Length || paragraph[index + 1] == ' '))
            {
                yield return (from, index + 1);
                from = index + 2;
            }
        }

        if (from < paragraph.Length)
        {
            yield return (from, paragraph.Length);
        }
    }

    [GeneratedRegex(@"\b(?:MUST NOT|MUST|SHALL NOT|SHALL|SHOULD NOT|SHOULD|NOT RECOMMENDED|RECOMMENDED|REQUIRED|MAY|OPTIONAL)\b")]
    private static partial Regex Keyword();

    [GeneratedRegex(@"\[Page \d+\]\s*$")]
    private static partial Regex PageFooter();

    [GeneratedRegex(@"^RFC \d+\s")]
    private static partial Regex PageHeader();

    private readonly record struct SourceLine(int Start, int End)
    {
        public string Text(string raw) => raw[Start..End];

        public bool IsBlank(string raw) => string.IsNullOrWhiteSpace(Text(raw));

        public static List<SourceLine> Split(string raw)
        {
            var lines = new List<SourceLine>();
            var start = 0;

            while (start <= raw.Length)
            {
                var end = raw.IndexOf('\n', start);
                var next = end < 0 ? raw.Length + 1 : end + 1;

                if (end < 0)
                {
                    end = raw.Length;
                }

                lines.Add(new SourceLine(start, end > start && raw[end - 1] == '\r' ? end - 1 : end));
                start = next;
            }

            return lines;
        }

        public static int IndexOf(IReadOnlyList<SourceLine> lines, int offset)
        {
            var low = 0;
            var high = lines.Count - 1;

            while (low < high)
            {
                var middle = low + ((high - low + 1) / 2);

                if (lines[middle].Start <= offset)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return low;
        }
    }
}
