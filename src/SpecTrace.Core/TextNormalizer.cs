using System.Text;

namespace SpecTrace.Core;

public static class TextNormalizer
{
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = new StringBuilder(text.Length);
        var runPending = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
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

        return normalized.ToString();
    }
}
