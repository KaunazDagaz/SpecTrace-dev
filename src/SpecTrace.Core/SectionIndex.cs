using System.Text.RegularExpressions;

namespace SpecTrace.Core;

public sealed record SectionHeader(int RawOffset, string Number, string Title);

public sealed partial class SectionIndex
{
    public const string FrontMatter = "(front matter)";

    private readonly SectionHeader[] _headers;

    private SectionIndex(SectionHeader[] headers) => _headers = headers;

    public IReadOnlyList<SectionHeader> Headers => _headers;

    public static SectionIndex Build(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var headers = new List<SectionHeader>();
        var lineStart = 0;

        while (lineStart <= raw.Length)
        {
            var lineEnd = raw.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = raw.Length;
            }

            var line = raw[lineStart..lineEnd];

            if (line.EndsWith('\r'))
            {
                line = line[..^1];
            }

            if (TryParseHeader(line, lineStart) is { } header)
            {
                headers.Add(header);
            }

            if (lineEnd == raw.Length)
            {
                break;
            }

            lineStart = lineEnd + 1;
        }

        return new SectionIndex([.. headers]);
    }

    public string SectionFor(TextSpan span) => SectionFor(span.Start);

    public string SectionFor(int rawOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rawOffset);

        var low = 0;
        var high = _headers.Length - 1;
        var found = -1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);

            if (_headers[middle].RawOffset <= rawOffset)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found < 0 ? FrontMatter : _headers[found].Number;
    }

    private static SectionHeader? TryParseHeader(string line, int rawOffset)
    {
        var appendix = AppendixHeader().Match(line);
        if (appendix.Success)
        {
            return new SectionHeader(rawOffset, appendix.Groups[1].Value, appendix.Groups[2].Value.TrimEnd());
        }

        var numbered = NumberedHeader().Match(line);
        return numbered.Success
            ? new SectionHeader(rawOffset, numbered.Groups[1].Value, numbered.Groups[2].Value.TrimEnd())
            : null;
    }

    [GeneratedRegex(@"^((?:\d+|[A-Z])(?:\.\d+)*)\.[ \t]+(\S.*)$")]
    private static partial Regex NumberedHeader();

    [GeneratedRegex(@"^Appendix[ \t]+([A-Z])\.[ \t]+(\S.*)$")]
    private static partial Regex AppendixHeader();
}
