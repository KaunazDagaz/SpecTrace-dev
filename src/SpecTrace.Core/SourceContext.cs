namespace SpecTrace.Core;

public sealed record SourceContext(string Before, string Quote, string After)
{
    public const int DefaultReach = 600;

    public static SourceContext Around(string raw, TextSpan span, int reach = DefaultReach)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentOutOfRangeException.ThrowIfNegative(reach);

        if (span.Start < 0 || span.End > raw.Length || span.Length <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(span),
                span,
                $"The span must lie inside the document's {raw.Length} characters.");
        }

        var from = LineStart(raw, span.Start);

        while (from > 0)
        {
            var previous = LineStart(raw, from - 1);

            if (string.IsNullOrWhiteSpace(raw[previous..(from - 1)]) || span.Start - previous > reach)
            {
                break;
            }

            from = previous;
        }

        var to = LineEnd(raw, span.End);

        while (to < raw.Length)
        {
            var nextStart = to + 1;
            var nextEnd = LineEnd(raw, nextStart);

            if (string.IsNullOrWhiteSpace(raw[nextStart..nextEnd]) || nextEnd - span.End > reach)
            {
                break;
            }

            to = nextEnd;
        }

        return new SourceContext(raw[from..span.Start], raw[span.Start..span.End], raw[span.End..to]);
    }

    private static int LineStart(string raw, int index) =>
        index == 0 ? 0 : raw.LastIndexOf('\n', index - 1) + 1;

    private static int LineEnd(string raw, int index)
    {
        var newline = raw.IndexOf('\n', index);

        return newline < 0 ? raw.Length : newline;
    }
}
