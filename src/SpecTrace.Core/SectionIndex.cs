using System.Text.RegularExpressions;

namespace SpecTrace.Core;

/// <summary>One parsed section header and where it starts in the raw document.</summary>
public sealed record SectionHeader(int RawOffset, string Number, string Title);

/// <summary>
/// The document's header structure, used to name the section any span falls in.
/// </summary>
/// <remarks>
/// <para>
/// Headers are recognised only at column 0. Every header-shaped line that is indented —
/// in RFC 6902 that is the whole table of contents — is therefore not a header. Without
/// that anchor the table of contents would register 34 phantom sections at the top of
/// the document and every real lookup would be wrong.
/// </para>
/// <para>
/// The pattern in implementation plan §5.5 covers numbered headers only. Checked against
/// the corpus it misses <c>Appendix A.</c> and <c>A.1.</c>–<c>A.16.</c>, which would leave
/// everything from the appendix to the end of the file labelled with the last numbered
/// section — roughly 38% of RFC 6902, wrong and silent. Appendix headers are matched here
/// for that reason.
/// </para>
/// </remarks>
public sealed partial class SectionIndex
{
    /// <summary>Reported for a span that precedes the first header, rather than an empty string.</summary>
    public const string FrontMatter = "(front matter)";

    private readonly SectionHeader[] _headers;

    private SectionIndex(SectionHeader[] headers) => _headers = headers;

    /// <summary>The headers, in document order.</summary>
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

            // Scanning line by line rather than with a multiline regex keeps a CRLF
            // document from carrying a stray CR into the captured title.
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

        // Built in document order, so already sorted by offset.
        return new SectionIndex([.. headers]);
    }

    /// <summary>Names the section a span falls in: the last header at or before its start.</summary>
    public string SectionFor(TextSpan span) => SectionFor(span.Start);

    /// <summary>Names the section a raw offset falls in: the last header at or before it.</summary>
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

    /// <summary>
    /// Matches <c>1.</c>, <c>4.1.</c> and <c>A.1.</c> through <c>A.16.</c> at column 0.
    /// </summary>
    /// <remarks>
    /// The trailing dot is required. Without it a single leading capital would also match
    /// the running page headers that sit at column 0 on every RFC page — <c>RFC 6902 ...</c>
    /// and <c>Bryan &amp; Nottingham ... [Page n]</c> — and each of the 18 page breaks would
    /// register as a section.
    /// </remarks>
    [GeneratedRegex(@"^((?:\d+|[A-Z])(?:\.\d+)*)\.[ \t]+(\S.*)$")]
    private static partial Regex NumberedHeader();

    /// <summary>Matches the appendix's own title line, <c>Appendix A.  Examples</c>.</summary>
    [GeneratedRegex(@"^Appendix[ \t]+([A-Z])\.[ \t]+(\S.*)$")]
    private static partial Regex AppendixHeader();
}
