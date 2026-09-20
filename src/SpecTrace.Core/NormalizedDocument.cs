using System.Text;

namespace SpecTrace.Core;

/// <summary>
/// A source document paired with its normalised form and a map from every normalised
/// character back to the raw character it came from.
/// </summary>
/// <remarks>
/// RFC <c>.txt</c> files are hard-wrapped at roughly 72 columns, so a sentence that a
/// model quotes as one line contains newlines in the raw file. Searching the raw text
/// directly therefore fails on almost every <em>correct</em> quote. Normalising both
/// sides and mapping back is what makes the span exact rather than approximate —
/// implementation plan §4.2.
/// </remarks>
public sealed class NormalizedDocument
{
    private readonly int[] _map;

    private NormalizedDocument(string raw, string normal, int[] map)
    {
        Raw = raw;
        Normal = normal;
        _map = map;
    }

    /// <summary>The original file content, untouched.</summary>
    public string Raw { get; }

    /// <summary>The normalised form: whitespace runs collapsed to a single space, trimmed.</summary>
    public string Normal { get; }

    /// <summary><c>Map[i]</c> is the index in <see cref="Raw"/> of the character at <c>Normal[i]</c>.</summary>
    public ReadOnlySpan<int> Map => _map;

    public static NormalizedDocument Create(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var normal = new StringBuilder(raw.Length);
        var map = new List<int>(raw.Length);

        // -1 means "no whitespace run is open". When one is open this holds the raw
        // index of its FIRST character: the collapsed space maps to the start of the
        // run, not its end. Mapping to the end is equally defensible and silently
        // moves span edges, so the choice is pinned by a test.
        var runStart = -1;

        for (var i = 0; i < raw.Length; i++)
        {
            if (char.IsWhiteSpace(raw[i]))
            {
                // Leading whitespace never opens a run, which is what trims the start.
                if (runStart < 0 && normal.Length > 0)
                {
                    runStart = i;
                }

                continue;
            }

            if (runStart >= 0)
            {
                normal.Append(' ');
                map.Add(runStart);
                runStart = -1;
            }

            normal.Append(raw[i]);
            map.Add(i);
        }

        // A run left open at the end is never flushed, which trims the end.
        return new NormalizedDocument(raw, normal.ToString(), [.. map]);
    }

    /// <summary>
    /// Locates a quote in this document, per implementation plan §4.2.
    /// </summary>
    /// <remarks>
    /// Exact substring matching only. There is no fuzzy path, and a quote found more
    /// than once is reported as ambiguous rather than resolved to its first match.
    /// </remarks>
    public QuoteResolution Resolve(string quote)
    {
        ArgumentNullException.ThrowIfNull(quote);

        var normalizedQuote = TextNormalizer.Normalize(quote);

        // An empty quote would match at index 0 and hand back a plausible zero-length
        // span at the start of the document — a silent success. Fail closed instead.
        if (normalizedQuote.Length == 0)
        {
            return QuoteResolution.Failed(normalizedQuote);
        }

        var first = Normal.IndexOf(normalizedQuote, StringComparison.Ordinal);
        if (first < 0)
        {
            return QuoteResolution.Failed(normalizedQuote);
        }

        // Start one past the first hit so overlapping repeats count as repeats.
        if (Normal.IndexOf(normalizedQuote, first + 1, StringComparison.Ordinal) >= 0)
        {
            return QuoteResolution.Ambiguous(normalizedQuote);
        }

        var span = new TextSpan(_map[first], _map[first + normalizedQuote.Length - 1] + 1);
        return QuoteResolution.Exact(normalizedQuote, span);
    }
}
