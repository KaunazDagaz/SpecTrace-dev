using System.Text;

namespace SpecTrace.Core;

/// <summary>
/// The one whitespace normalisation rule in the system: every run of whitespace
/// collapses to a single space, and the result is trimmed.
/// </summary>
/// <remarks>
/// Both the document and every quote looked up against it pass through here. If the
/// two ever normalised differently, correct quotes would fail to resolve and it would
/// look like the model had hallucinated them — see implementation plan §4.2.
/// </remarks>
public static class TextNormalizer
{
    /// <summary>
    /// Collapses whitespace runs to a single space and trims the result.
    /// </summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = new StringBuilder(text.Length);
        var runPending = false;

        foreach (var character in text)
        {
            // char.IsWhiteSpace folds tabs, CR, LF and the form feeds that separate
            // RFC pages into one rule, so CRLF costs nothing special here.
            if (char.IsWhiteSpace(character))
            {
                // Leading whitespace never opens a run, which is what trims the start.
                runPending = normalized.Length > 0;
                continue;
            }

            if (runPending)
            {
                normalized.Append(' ');
                runPending = false;
            }

            normalized.Append(character);
        }

        // A run left open at the end is never flushed, which trims the end.
        return normalized.ToString();
    }
}
