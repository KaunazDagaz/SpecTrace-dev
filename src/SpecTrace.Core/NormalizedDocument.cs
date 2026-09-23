using System.Text;

namespace SpecTrace.Core;

public sealed class NormalizedDocument
{
    private readonly int[] _map;

    private NormalizedDocument(string raw, string normal, int[] map)
    {
        Raw = raw;
        Normal = normal;
        _map = map;
    }

    public string Raw { get; }

    public string Normal { get; }

    public ReadOnlySpan<int> Map => _map;

    public static NormalizedDocument Create(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var normal = new StringBuilder(raw.Length);
        var map = new List<int>(raw.Length);

        var runStart = -1;

        for (var i = 0; i < raw.Length; i++)
        {
            if (char.IsWhiteSpace(raw[i]))
            {
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

        return new NormalizedDocument(raw, normal.ToString(), [.. map]);
    }

    public QuoteResolution Resolve(string quote)
    {
        ArgumentNullException.ThrowIfNull(quote);

        var normalizedQuote = TextNormalizer.Normalize(quote);

        if (normalizedQuote.Length == 0)
        {
            return QuoteResolution.Failed(normalizedQuote);
        }

        var first = Normal.IndexOf(normalizedQuote, StringComparison.Ordinal);
        if (first < 0)
        {
            return QuoteResolution.Failed(normalizedQuote);
        }

        if (Normal.IndexOf(normalizedQuote, first + 1, StringComparison.Ordinal) >= 0)
        {
            return QuoteResolution.Ambiguous(normalizedQuote);
        }

        var span = new TextSpan(_map[first], _map[first + normalizedQuote.Length - 1] + 1);
        return QuoteResolution.Exact(normalizedQuote, span);
    }
}
