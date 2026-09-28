using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public static class ReviewSummary
{
    public static string Render(string logName, byte[] logBytes, ReviewOutcomes outcomes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logName);
        ArgumentNullException.ThrowIfNull(logBytes);
        ArgumentNullException.ThrowIfNull(outcomes);

        var output = new StringBuilder();
        var runs = outcomes.RunIds.Count == 0 ? "no run" : string.Join(", ", outcomes.RunIds);

        Line(output, $"# Review outcomes: {runs}");
        Line(output);
        Line(output, "Computed from the review log alone by `spectrace score --reviews`. No model was called.");
        Line(output);
        Line(output, $"- Log: `{logName}`, SHA-256 `{Convert.ToHexStringLower(SHA256.HashData(logBytes))}`, {outcomes.Lines} lines");
        Line(output, $"- Authors, self-declared and not verified: {(outcomes.Authors.Count == 0 ? "none" : string.Join(", ", outcomes.Authors))}");
        Line(output,
            $"- Test cases decided: {outcomes.CasesDecided}; accepted as proposed {Share(outcomes.CasesAccepted, outcomes.CasesDecided)}, "
            + $"edited {Share(outcomes.CasesEdited, outcomes.CasesDecided)}, rejected {Share(outcomes.CasesRejected, outcomes.CasesDecided)}");
        Line(output,
            $"- Decision-queue items decided: {outcomes.ItemsDecided}; testable {outcomes.ItemsTestable}, "
            + $"not testable {outcomes.ItemsNotTestable}, deferred {outcomes.ItemsDeferred}");
        Line(output, $"- Superseded decisions, followed by a later line on the same case or item: {outcomes.Superseded}");
        Line(output);
        Line(output,
            "Each case and item counts once, by its latest decision in the log. Cases no one decided are not "
            + "in the log and not counted here.");

        return output.ToString();
    }

    private static string Share(int part, int whole) =>
        whole == 0
            ? part.ToString(CultureInfo.InvariantCulture)
            : $"{part.ToString(CultureInfo.InvariantCulture)} ({((double)part / whole).ToString("P1", CultureInfo.InvariantCulture)})";

    private static void Line(StringBuilder output, string text = "") => output.Append(text).Append('\n');
}
