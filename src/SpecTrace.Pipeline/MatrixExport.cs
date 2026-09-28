using System.Globalization;
using System.Text;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public sealed record ReviewedRow(
    Requirement Requirement,
    CoverageStatus Status,
    IReadOnlyList<ReviewedCase> Covering,
    IReadOnlyList<ReviewedCase> Rejected,
    IReadOnlyList<ReviewedItem> Items);

public static class MatrixExport
{
    public const string MarkdownFormat = "markdown";

    public const string CsvFormat = "csv";

    public static readonly IReadOnlyList<string> CsvColumns =
        ["requirement_id", "section", "modality", "status", "test_cases", "rejected_test_cases", "quote"];

    public static IReadOnlyList<ReviewedRow> RowsOf(LoadedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var requirements = run.Contents.Register.ToDictionary(requirement => requirement.Id, StringComparer.Ordinal);
        var cases = run.Reviewed.Cases.ToDictionary(reviewed => reviewed.Original.Id, StringComparer.Ordinal);

        return run.Reviewed.Matrix.Rows
            .Select(row => new ReviewedRow(
                requirements[row.RequirementId],
                row.Status,
                row.TestCaseIds.Select(id => cases[id]).ToList().AsReadOnly(),
                run.Reviewed.Cases
                    .Where(reviewed => reviewed.Current.Status == ReviewStatus.Rejected
                        && reviewed.Current.RequirementIds.Contains(row.RequirementId, StringComparer.Ordinal))
                    .OrderBy(reviewed => reviewed.Original.Id, StringComparer.Ordinal)
                    .ToList()
                    .AsReadOnly(),
                run.Reviewed.Items
                    .Where(item => item.Entry.RequirementId == row.RequirementId)
                    .ToList()
                    .AsReadOnly()))
            .ToList()
            .AsReadOnly();
    }

    public static string Render(LoadedRun run, string format) => format switch
    {
        MarkdownFormat => Markdown(run),
        CsvFormat => Csv(run),
        _ => throw new ArgumentException($"'{format}' is not an export format; use {MarkdownFormat} or {CsvFormat}.", nameof(format)),
    };

    public static string Markdown(LoadedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var rows = RowsOf(run);
        var cases = run.Reviewed.Cases;
        var output = new StringBuilder();

        Line(output, $"# Reviewed traceability matrix: {run.DocumentId}");
        Line(output);
        Line(output, $"- Run: `{run.RunId}` (`{run.Key}`)");
        Line(output, $"- Review log: {run.Log.Count} decisions, authors self-declared");
        Line(output, $"- Requirements in the register: {rows.Count}");
        Line(output, $"- Covered by at least one non-rejected test case: {Count(rows, CoverageStatus.Covered)}");
        Line(output, $"- Gaps: {Count(rows, CoverageStatus.Gap)}");
        Line(output, $"- Not testable, decided by a person: {Count(rows, CoverageStatus.NotTestable)}");
        Line(output, $"- Deferred by a person: {Count(rows, CoverageStatus.DeferredByHuman)}");
        Line(output,
            $"- Test cases: {cases.Count}; not yet reviewed {CountCases(cases, ReviewStatus.Proposed)}, "
            + $"accepted {CountCases(cases, ReviewStatus.Accepted)}, edited {CountCases(cases, ReviewStatus.Edited)}, "
            + $"rejected {CountCases(cases, ReviewStatus.Rejected)}");
        Line(output);
        Line(output,
            $"{MatrixHtml.NoCompletenessClaim} It shows only whether each requirement in the register has a "
            + "test case that has not been rejected; a case nobody has reviewed yet still counts.");
        Line(output);
        Line(output, "| Requirement | Section | Modality | Status | Test cases | Rejected test cases | Quote |");
        Line(output, "|---|---|---|---|---|---|---|");

        foreach (var row in rows)
        {
            Line(output,
                $"| {Cell(row.Requirement.Id)} | {Cell(row.Requirement.Section)} | {Cell(RunArtifacts.Spell(row.Requirement.Modality))} "
                + $"| {Cell(RunArtifacts.Spell(row.Status))} | {Cell(CaseList(row.Covering, "; "))} "
                + $"| {Cell(CaseList(row.Rejected, "; "))} | {Cell(TextNormalizer.Normalize(row.Requirement.Text))} |");
        }

        Line(output);
        Line(output, $"## Orphan test cases: {run.Reviewed.Matrix.Orphans.Count}");
        Line(output);

        foreach (var orphan in run.Reviewed.Matrix.Orphans)
        {
            Line(output, $"- {orphan.TestCaseId}: names {string.Join(", ", orphan.MissingRequirementIds)}, not in the register");
        }

        if (run.Reviewed.Matrix.Orphans.Count == 0)
        {
            Line(output, "None.");
        }

        Line(output);
        Line(output, $"## Orphan decisions: {run.Reviewed.OrphanDecisions.Count}");
        Line(output);

        foreach (var orphan in run.Reviewed.OrphanDecisions)
        {
            Line(output, $"- log line {orphan.Line.ToString(CultureInfo.InvariantCulture)}, {orphan.Decision.TargetId}: {orphan.Reason}");
        }

        if (run.Reviewed.OrphanDecisions.Count == 0)
        {
            Line(output, "None.");
        }

        return output.ToString();
    }

    public static string Csv(LoadedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var output = new StringBuilder();

        Line(output, string.Join(",", CsvColumns));

        foreach (var row in RowsOf(run))
        {
            Line(output, string.Join(",", new[]
            {
                row.Requirement.Id,
                row.Requirement.Section,
                RunArtifacts.Spell(row.Requirement.Modality),
                RunArtifacts.Spell(row.Status),
                CaseList(row.Covering, ";"),
                CaseList(row.Rejected, ";"),
                TextNormalizer.Normalize(row.Requirement.Text),
            }.Select(CsvField)));
        }

        return output.ToString();
    }

    public static string CaseLabel(ReviewedCase reviewed) =>
        $"{reviewed.Original.Id} {RunArtifacts.Spell(reviewed.Current.Status)}";

    private static string CaseList(IReadOnlyList<ReviewedCase> cases, string separator) =>
        string.Join(separator, cases.Select(CaseLabel));

    private static int Count(IReadOnlyList<ReviewedRow> rows, CoverageStatus status) =>
        rows.Count(row => row.Status == status);

    private static int CountCases(IReadOnlyList<ReviewedCase> cases, ReviewStatus status) =>
        cases.Count(reviewed => reviewed.Current.Status == status);

    private static string Cell(string text) =>
        text.Length == 0 ? "—" : text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal);

    private static string CsvField(string text) =>
        text.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : text;

    private static void Line(StringBuilder output, string text = "") => output.Append(text).Append('\n');
}
