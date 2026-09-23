using System.Net;
using System.Text;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public static class MatrixHtml
{
    public const string NoCompletenessClaim =
        "This matrix does not claim that the specification is fully covered.";

    private static readonly string[] StyleRules =
    [
        "body { font-family: sans-serif; margin: 1.5rem; line-height: 1.4; }",
        "table { border-collapse: collapse; width: 100%; margin-bottom: 1.5rem; }",
        "th, td { border: 1px solid #999; padding: 0.3rem 0.5rem; text-align: left; vertical-align: top; }",
        ".notice { border: 2px solid #333; padding: 0.5rem 0.75rem; }",
        "#decision-queue { border: 3px solid #b35900; background: #fff3e0; padding: 0 1rem; margin-bottom: 1.5rem; }",
        "tr.gap { background: #ffdede; }",
        "td.gap { font-weight: bold; color: #a00000; }",
    ];

    public static string Render(
        string documentId,
        IReadOnlyList<Requirement> register,
        IReadOnlyList<TestCase> cases,
        TraceabilityMatrix matrix,
        IReadOnlyList<QueuedDecision> decisionQueue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(decisionQueue);

        var requirements = register.ToDictionary(requirement => requirement.Id, StringComparer.Ordinal);
        var casesById = cases.ToDictionary(testCase => testCase.Id, StringComparer.Ordinal);
        var queuedByRequirement = decisionQueue
            .Where(decision => decision.RequirementId is not null)
            .GroupBy(decision => decision.RequirementId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(decision => decision.Item.Id).ToList(), StringComparer.Ordinal);

        var page = new StringBuilder();

        WriteHead(page, documentId);
        WriteSummary(page, cases, matrix, decisionQueue);
        WriteDecisionQueue(page, decisionQueue);
        WriteMatrix(page, matrix, requirements, casesById, queuedByRequirement);
        WriteOrphans(page, matrix);
        WriteCases(page, cases, matrix);

        Line(page, "</body>");
        Line(page, "</html>");

        return page.ToString();
    }

    private static void WriteHead(StringBuilder page, string documentId)
    {
        Line(page, "<!DOCTYPE html>");
        Line(page, "<html lang=\"en\">");
        Line(page, "<head>");
        Line(page, "<meta charset=\"utf-8\">");
        Line(page, $"<title>Traceability matrix: {Encode(documentId)}</title>");
        Line(page, "<style>");

        foreach (var rule in StyleRules)
        {
            Line(page, rule);
        }

        Line(page, "</style>");
        Line(page, "</head>");
        Line(page, "<body>");
        Line(page, $"<h1>Traceability matrix: {Encode(documentId)}</h1>");
    }

    private static void WriteSummary(
        StringBuilder page,
        IReadOnlyList<TestCase> cases,
        TraceabilityMatrix matrix,
        IReadOnlyList<QueuedDecision> decisionQueue)
    {
        var unreviewed = cases.Count(testCase => testCase.Status == ReviewStatus.Proposed);
        var covered = matrix.Rows.Count(row => row.Status == CoverageStatus.Covered);
        var gaps = matrix.Rows.Count(row => row.Status == CoverageStatus.Gap);
        var decidedByPerson = matrix.Rows.Count(row =>
            row.Status is CoverageStatus.NotTestable or CoverageStatus.DeferredByHuman);

        Line(page, "<div class=\"notice\">");
        Line(page,
            "<p><strong>Coverage here is by proposed, unreviewed test cases.</strong> "
            + $"{unreviewed} of the {cases.Count} test cases have not been reviewed by a person. A model "
            + "wrote every case from one requirement's quote and section number alone.</p>");
        Line(page,
            $"<p><strong>{NoCompletenessClaim}</strong> It shows only whether each requirement in the "
            + "register has a proposed case. A requirement the extraction did not find is not in the "
            + "register and cannot appear here.</p>");
        Line(page, "</div>");

        Line(page, "<ul>");
        Line(page, $"<li>Requirements in the register: {matrix.Rows.Count}</li>");
        Line(page, $"<li>Covered by at least one proposed test case: {covered}</li>");
        Line(page, $"<li><a href=\"#matrix\"><strong>Gaps: {gaps}</strong></a> (requirements with no test case)</li>");
        Line(page, $"<li>Marked not testable or deferred by a person: {decidedByPerson}</li>");
        Line(page, $"<li><a href=\"#decision-queue\"><strong>Items in the decision queue: {decisionQueue.Count}</strong></a></li>");
        Line(page, $"<li><a href=\"#orphans\">Orphan test cases: {matrix.Orphans.Count}</a></li>");
        Line(page, $"<li><a href=\"#cases\">Proposed test cases: {cases.Count}</a></li>");
        Line(page, "</ul>");
    }

    private static void WriteDecisionQueue(StringBuilder page, IReadOnlyList<QueuedDecision> decisionQueue)
    {
        Line(page, "<section id=\"decision-queue\">");
        Line(page, $"<h2>Decision queue: {decisionQueue.Count} items for a person</h2>");
        Line(page, "<p>The system is not allowed to decide these alone. Each item is a question, not an answer.</p>");

        if (decisionQueue.Count == 0)
        {
            Line(page, "<p>The queue is empty.</p>");
            Line(page, "</section>");
            return;
        }

        Line(page, "<table>");
        Line(page, "<thead><tr><th>Item</th><th>Section</th><th>Quote</th><th>Reason</th><th>Model's note</th><th>Question</th></tr></thead>");
        Line(page, "<tbody>");

        foreach (var decision in decisionQueue)
        {
            var item = decision.RequirementId is null
                ? Encode(decision.Item.Id)
                : $"{Encode(decision.Item.Id)}<br>{Encode(decision.RequirementId)}";

            Line(page,
                $"<tr><td>{item}</td><td>{Encode(decision.Item.Section)}</td>"
                + $"<td>{Encode(TextNormalizer.Normalize(decision.Item.Quote))}</td>"
                + $"<td>{Encode(decision.Reason)}</td><td>{Encode(ModelNote(decision))}</td>"
                + $"<td>{Encode(decision.Item.Question)}</td></tr>");
        }

        Line(page, "</tbody>");
        Line(page, "</table>");
        Line(page, "</section>");
    }

    private static void WriteMatrix(
        StringBuilder page,
        TraceabilityMatrix matrix,
        Dictionary<string, Requirement> requirements,
        Dictionary<string, TestCase> casesById,
        Dictionary<string, List<string>> queuedByRequirement)
    {
        Line(page, "<h2 id=\"matrix\">Matrix</h2>");
        Line(page, "<p>One row per requirement in the register, in the order the requirements appear in the document.</p>");
        Line(page, "<table>");
        Line(page, "<thead><tr><th>Requirement</th><th>Section</th><th>Modality</th><th>Quote</th><th>Proposed test cases</th><th>Status</th></tr></thead>");
        Line(page, "<tbody>");

        foreach (var row in matrix.Rows)
        {
            var quote = TextNormalizer.Normalize(requirements[row.RequirementId].Text);
            var caseCell = row.TestCaseIds.Count == 0
                ? "none"
                : string.Join("<br>", row.TestCaseIds.Select(id =>
                    $"{Encode(id)} ({Encode(RunArtifacts.Spell(casesById[id].Type))})"));

            var status = StatusLabel(row.Status);

            if (queuedByRequirement.TryGetValue(row.RequirementId, out var queued))
            {
                status = $"{status}<br>see {string.Join(", ", queued.Select(Encode))}";
            }

            var rowClass = row.Status == CoverageStatus.Gap ? " class=\"gap\"" : string.Empty;
            var statusClass = row.Status == CoverageStatus.Gap ? " class=\"gap\"" : string.Empty;

            Line(page,
                $"<tr{rowClass}><td>{Encode(row.RequirementId)}</td><td>{Encode(row.Section)}</td>"
                + $"<td>{Encode(RunArtifacts.Spell(row.Modality))}</td><td>{Encode(quote)}</td>"
                + $"<td>{caseCell}</td><td{statusClass}>{status}</td></tr>");
        }

        Line(page, "</tbody>");
        Line(page, "</table>");
    }

    private static void WriteOrphans(StringBuilder page, TraceabilityMatrix matrix)
    {
        Line(page, $"<h2 id=\"orphans\">Orphan test cases: {matrix.Orphans.Count}</h2>");

        if (matrix.Orphans.Count == 0)
        {
            Line(page, "<p>None. Every test case names only requirements that are in the register.</p>");
            return;
        }

        Line(page, "<p>Test cases that name a requirement which is not in the register. They are listed here rather than dropped.</p>");
        Line(page, "<table>");
        Line(page, "<thead><tr><th>Test case</th><th>Names</th><th>Missing from the register</th></tr></thead>");
        Line(page, "<tbody>");

        foreach (var orphan in matrix.Orphans)
        {
            Line(page,
                $"<tr><td>{Encode(orphan.TestCaseId)}</td>"
                + $"<td>{string.Join("<br>", orphan.RequirementIds.Select(Encode))}</td>"
                + $"<td>{string.Join("<br>", orphan.MissingRequirementIds.Select(Encode))}</td></tr>");
        }

        Line(page, "</tbody>");
        Line(page, "</table>");
    }

    private static void WriteCases(StringBuilder page, IReadOnlyList<TestCase> cases, TraceabilityMatrix matrix)
    {
        Line(page, $"<h2 id=\"cases\">Proposed test cases: {cases.Count}</h2>");

        if (cases.Count == 0)
        {
            Line(page, "<p>None.</p>");
            return;
        }

        Line(page, "<table>");
        Line(page, "<thead><tr><th>Test case</th><th>Requirement</th><th>Type</th><th>Title</th><th>Precondition</th><th>Input</th><th>Expected result</th><th>Status</th></tr></thead>");
        Line(page, "<tbody>");

        foreach (var testCase in InMatrixOrder(cases, matrix))
        {
            Line(page,
                $"<tr><td>{Encode(testCase.Id)}</td>"
                + $"<td>{string.Join("<br>", testCase.RequirementIds.Select(Encode))}</td>"
                + $"<td>{Encode(RunArtifacts.Spell(testCase.Type))}</td><td>{Encode(testCase.Title)}</td>"
                + $"<td>{Encode(testCase.Precondition)}</td><td>{Encode(testCase.Input)}</td>"
                + $"<td>{Encode(testCase.ExpectedResult)}</td><td>{Encode(RunArtifacts.Spell(testCase.Status))}</td></tr>");
        }

        Line(page, "</tbody>");
        Line(page, "</table>");
    }

    private static IEnumerable<TestCase> InMatrixOrder(IReadOnlyList<TestCase> cases, TraceabilityMatrix matrix)
    {
        var listed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in matrix.Rows)
        {
            var named = cases
                .Where(testCase => testCase.RequirementIds.Contains(row.RequirementId, StringComparer.Ordinal))
                .OrderBy(testCase => testCase.Id, StringComparer.Ordinal);

            foreach (var testCase in named)
            {
                if (listed.Add(testCase.Id))
                {
                    yield return testCase;
                }
            }
        }

        foreach (var testCase in cases.OrderBy(testCase => testCase.Id, StringComparer.Ordinal))
        {
            if (listed.Add(testCase.Id))
            {
                yield return testCase;
            }
        }
    }

    private static string ModelNote(QueuedDecision decision) =>
        decision.BlockedReason
        ?? string.Join(
            " ",
            decision.Claims
                .Select(claim => claim.TestabilityNote)
                .Where(note => !string.IsNullOrWhiteSpace(note))
                .Distinct(StringComparer.Ordinal));

    private static string StatusLabel(CoverageStatus status) => status switch
    {
        CoverageStatus.Covered => "covered",
        CoverageStatus.Gap => "GAP",
        CoverageStatus.DeferredByHuman => "deferred by a person",
        CoverageStatus.NotTestable => "not testable, decided by a person",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    private static void Line(StringBuilder page, string text) => page.Append(text).Append('\n');
}
