using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

public sealed class WebReviewTests
{
    private const string ReviewPage = "/runs/reference/review";

    [Fact]
    public async Task EachReviewActionAppendsExactlyOneLineAndLeavesEveryEarlierLineByteIdentical()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        await using var web = await WebApp.StartAsync(ReviewWorkspace.Host(workspace));
        var log = ReviewWorkspace.LogOf(workspace);

        await web.SetReviewerAsync(Workspace.ReferenceKey, ReviewWorkspace.Reviewer);

        (string Handler, KeyValuePair<string, string>[] Fields, string Target, string Id, string Decision)[] actions =
        [
            ("Case", ReviewWorkspace.Case("TC-07e6ff-01", "accept"), ReviewLog.TestCaseTarget, "TC-07e6ff-01", "accept"),
            ("Case", ReviewWorkspace.Edit("TC-07e6ff-02", "An operation object with two op members is rejected"), ReviewLog.TestCaseTarget, "TC-07e6ff-02", "edit"),
            ("Case", ReviewWorkspace.Case("TC-07e6ff-03", "reject"), ReviewLog.TestCaseTarget, "TC-07e6ff-03", "reject"),
            ("Item", ReviewWorkspace.Item("DQ-rfc6902-5a829e", "testable"), ReviewLog.QueueItemTarget, "DQ-rfc6902-5a829e", "testable"),
            ("Item", ReviewWorkspace.Item("DQ-rfc6902-5a829e", "not_testable"), ReviewLog.QueueItemTarget, "DQ-rfc6902-5a829e", "not_testable"),
            ("Item", ReviewWorkspace.Item("DQ-rfc6902-1dd2c2", "defer"), ReviewLog.QueueItemTarget, "DQ-rfc6902-1dd2c2", "defer"),
            ("Case", ReviewWorkspace.Case("TC-07e6ff-03", "accept"), ReviewLog.TestCaseTarget, "TC-07e6ff-03", "accept"),
        ];

        var before = Array.Empty<byte>();

        foreach (var action in actions)
        {
            using var response = await web.PostAsync(ReviewPage, action.Handler, action.Fields);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

            var after = await File.ReadAllBytesAsync(log);

            Assert.True(after.Length > before.Length, $"{action.Id} {action.Decision} appended nothing.");
            Assert.True(after.AsSpan(0, before.Length).SequenceEqual(before), $"{action.Id} {action.Decision} changed an earlier line.");

            var added = after[before.Length..];

            Assert.Equal(1, added.Count(value => value == (byte)'\n'));
            Assert.Equal((byte)'\n', added[^1]);

            using var line = JsonDocument.Parse(added);
            var root = line.RootElement;

            Assert.Equal("rfc6902-3ff2234db6aa", root.GetProperty("run_id").GetString());
            Assert.Equal(action.Target, root.GetProperty("target").GetString());
            Assert.Equal(action.Id, root.GetProperty(action.Target == ReviewLog.TestCaseTarget ? "test_case_id" : "item_id").GetString());
            Assert.Equal(action.Decision, root.GetProperty("decision").GetString());
            Assert.Equal(ReviewWorkspace.Reviewer, root.GetProperty("author").GetString());
            Assert.True(root.GetProperty("author_self_declared").GetBoolean());
            Assert.True(DateTimeOffset.TryParse(root.GetProperty("at").GetString(), out _));

            before = after;
        }

        var decisions = ReviewLog.Parse(log, before);
        var edit = Assert.IsType<ReviewRecord>(decisions[1]);

        Assert.Equal(actions.Length, decisions.Count);
        Assert.Equal("An operation object with two op members is rejected", edit.Edit!.Title);
        Assert.Equal(CaseType.Negative, edit.Edit.Type);
        Assert.Equal("REQ-rfc6902-5a829e", Assert.IsType<QueueResolution>(decisions[4]).RequirementId);
        Assert.Null(Assert.IsType<QueueResolution>(decisions[5]).RequirementId);
    }

    [Fact]
    public async Task ARejectedCaseAndAHumanNotTestableDecisionChangeTheReviewedMatrixExactlyAsI4Says()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        await using var web = await WebApp.StartAsync(ReviewWorkspace.Host(workspace));

        var initial = await StatusesOnTheMatrixPageAsync(web);

        Assert.Equal("covered", initial["REQ-rfc6902-1d542c"]);
        Assert.Equal("gap", initial["REQ-rfc6902-5a829e"]);

        await web.SetReviewerAsync(Workspace.ReferenceKey, ReviewWorkspace.Reviewer);
        await PostAsync(web, "Case", ReviewWorkspace.Case("TC-1d542c-01", "reject"));

        Assert.Equal("covered", (await StatusesOnTheMatrixPageAsync(web))["REQ-rfc6902-1d542c"]);

        await PostAsync(web, "Case", ReviewWorkspace.Case("TC-1d542c-02", "reject"));
        await PostAsync(web, "Item", ReviewWorkspace.Item("DQ-rfc6902-5a829e", "not_testable"));

        var reviewed = await StatusesOnTheMatrixPageAsync(web);

        Assert.Equal("gap", reviewed["REQ-rfc6902-1d542c"]);
        Assert.Equal("not_testable", reviewed["REQ-rfc6902-5a829e"]);
        Assert.All(
            reviewed.Where(pair => pair.Key is not ("REQ-rfc6902-1d542c" or "REQ-rfc6902-5a829e")),
            pair => Assert.Equal(initial[pair.Key], pair.Value));

        await PostAsync(web, "Item", ReviewWorkspace.Item("DQ-rfc6902-5a829e", "testable"));
        await PostAsync(web, "Case", ReviewWorkspace.Case("TC-1d542c-02", "accept"));

        var changedAgain = await StatusesOnTheMatrixPageAsync(web);

        Assert.Equal("gap", changedAgain["REQ-rfc6902-5a829e"]);
        Assert.Equal("covered", changedAgain["REQ-rfc6902-1d542c"]);
    }

    [Fact]
    public async Task TheMarkdownAndCsvExportsHoldTheSameRowsAndStatusesAsTheMatrixPage()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        await using var web = await WebApp.StartAsync(ReviewWorkspace.Host(workspace));

        await web.SetReviewerAsync(Workspace.ReferenceKey, ReviewWorkspace.Reviewer);
        await PostAsync(web, "Case", ReviewWorkspace.Case("TC-1d542c-01", "reject"));
        await PostAsync(web, "Case", ReviewWorkspace.Case("TC-1d542c-02", "reject"));
        await PostAsync(web, "Item", ReviewWorkspace.Item("DQ-rfc6902-5a829e", "defer"));

        var page = await StatusesOnTheMatrixPageAsync(web);
        var csv = (await web.GetStringAsync("/runs/reference/matrix.csv")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var markdown = (await web.GetStringAsync("/runs/reference/matrix.md")).Split('\n')
            .Where(line => line.StartsWith("| REQ-", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(string.Join(",", MatrixExport.CsvColumns), csv[0]);
        Assert.Equal(page.Keys, csv[1..].Select(line => line.Split(',')[0]));
        Assert.Equal(page.Values, csv[1..].Select(line => line.Split(',')[3]));
        Assert.Equal(page.Keys, markdown.Select(line => line.Split(" | ")[0].TrimStart('|', ' ')));
        Assert.Equal(page.Values, markdown.Select(line => line.Split(" | ")[3]));
        Assert.Equal("deferred_by_human", page["REQ-rfc6902-5a829e"]);
        Assert.Equal("gap", page["REQ-rfc6902-1d542c"]);
    }

    [Fact]
    public async Task ASessionOfDecisionsLeavesThePipelinesFilesUnchangedAndTheGoldenComparisonStillPasses()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var reference = Repository.PathTo("runs", "reference");
        var copyBefore = RunComparison.Snapshot(workspace.Reference);
        var referenceBefore = RunComparison.Snapshot(reference);

        await using (var web = await WebApp.StartAsync(ReviewWorkspace.Host(workspace)))
        {
            await web.SetReviewerAsync(Workspace.ReferenceKey, ReviewWorkspace.Reviewer);

            var run = await new RunCatalog(workspace, TimeProvider.System).LoadAsync(Workspace.ReferenceKey, CancellationToken.None);
            var decisions = new[] { "accept", "reject", "edit" };
            var ordinal = 0;

            foreach (var testCase in run.Contents.Cases)
            {
                var decision = decisions[ordinal++ % decisions.Length];
                var fields = decision == "edit"
                    ? ReviewWorkspace.Edit(testCase.Id, $"{testCase.Title}, stated sharply")
                    : ReviewWorkspace.Case(testCase.Id, decision);

                await PostAsync(web, "Case", fields);
            }

            foreach (var item in run.Contents.DecisionQueue)
            {
                await PostAsync(web, "Item", ReviewWorkspace.Item(item.Item.Id, "not_testable"));
                await PostAsync(web, "Item", ReviewWorkspace.Item(item.Item.Id, "defer"));
            }

            Assert.Equal(
                run.Contents.Cases.Count + (2 * run.Contents.DecisionQueue.Count),
                ReviewLog.Parse(ReviewWorkspace.LogOf(workspace), await File.ReadAllBytesAsync(ReviewWorkspace.LogOf(workspace))).Count);
        }

        Assert.Equal(copyBefore, RunComparison.Snapshot(workspace.Reference));
        Assert.Equal(referenceBefore, RunComparison.Snapshot(reference));
        RunComparison.AssertSameArtifacts(reference, workspace.Reference);
        Assert.False(File.Exists(Path.Combine(workspace.Reference, "reviews.jsonl")));
    }

    [Fact]
    public async Task ADecisionThatCannotBeLoggedIsRefusedWithAReasonAndLeavesTheLogUnchanged()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        await using var web = await WebApp.StartAsync(ReviewWorkspace.Host(workspace));
        var log = ReviewWorkspace.LogOf(workspace);

        await web.SetReviewerAsync(Workspace.ReferenceKey, ReviewWorkspace.Reviewer);
        await PostAsync(web, "Case", ReviewWorkspace.Case("TC-07e6ff-01", "accept"));

        var logged = await File.ReadAllBytesAsync(log);
        var run = await new RunCatalog(workspace, TimeProvider.System).LoadAsync(Workspace.ReferenceKey, CancellationToken.None);
        var original = run.Contents.Cases.Single(testCase => testCase.Id == "TC-07e6ff-02");

        (string Handler, KeyValuePair<string, string>[] Fields, string Reason)[] refused =
        [
            ("Case", ReviewWorkspace.Case("TC-07e6ff-01", "accept", author: " "), "Enter your name"),
            ("Case", ReviewWorkspace.Case("TC-gone00-01", "reject"), "has no test case"),
            ("Case", ReviewWorkspace.Case("TC-07e6ff-01", "approve"), "not a CaseDecision"),
            ("Case", ReviewWorkspace.Edit("TC-07e6ff-01", " "), "Nothing was logged"),
            ("Case", ReviewWorkspace.Edit("TC-07e6ff-01", "A title", type: "sideways"), "not a CaseType"),
            ("Case",
                [
                    new("caseId", original.Id), new("decision", "edit"), new("author", ReviewWorkspace.Reviewer),
                    new("title", original.Title), new("type", RunArtifacts.Spell(original.Type)),
                    new("precondition", original.Precondition.Replace("\n", "\r\n", StringComparison.Ordinal)),
                    new("input", original.Input.Replace("\n", "\r\n", StringComparison.Ordinal)),
                    new("expectedResult", original.ExpectedResult),
                ],
                "changes nothing"),
            ("Item", ReviewWorkspace.Item("DQ-rfc6902-000000", "defer"), "has no decision-queue item"),
            ("Item", ReviewWorkspace.Item("DQ-rfc6902-5a829e", "maybe"), "not a QueueDecision"),
        ];

        foreach (var (handler, fields, reason) in refused)
        {
            using var response = await web.PostAsync(ReviewPage, handler, fields);
            var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(reason, page, StringComparison.Ordinal);
            Assert.Equal(logged, await File.ReadAllBytesAsync(log));
        }
    }

    [Fact]
    public async Task APostWithoutTheAntiforgeryTokenIsRejectedAndLogsNothing()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        await using var web = await WebApp.StartAsync(ReviewWorkspace.Host(workspace));

        using var response = await web.Client.PostAsync(
            $"{ReviewPage}?handler=Case",
            new FormUrlEncodedContent(ReviewWorkspace.Case("TC-07e6ff-01", "accept")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(File.Exists(ReviewWorkspace.LogOf(workspace)));
    }

    [Fact]
    public async Task TheReviewPageShowsEachQuoteHighlightedInItsSourceTextAndEncodesEveryValue()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);

        ReviewWorkspace.EditTestCases(workspace.Reference, cases =>
            cases[0]!["title"] = "<script>alert('review')</script> & <b>bold</b>");

        await using var web = await WebApp.StartAsync(ReviewWorkspace.Host(workspace));
        await web.SetReviewerAsync(Workspace.ReferenceKey, "<i>Reviewer</i>");

        var page = await web.GetStringAsync(ReviewPage);

        Assert.DoesNotContain("<script>alert", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>bold</b>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<i>Reviewer</i>", page, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(&#x27;review&#x27;)&lt;/script&gt; &amp; &lt;b&gt;bold&lt;/b&gt;", page, StringComparison.Ordinal);

        var run = await new RunCatalog(workspace, TimeProvider.System).LoadAsync(Workspace.ReferenceKey, CancellationToken.None);

        foreach (var requirement in run.Contents.Register)
        {
            var section = Regex.Match(
                page,
                $"<section class=\"requirement\" id=\"{requirement.Id}\">.*?<pre class=\"context\">(?<before>.*?)<mark>(?<quote>.*?)</mark>",
                RegexOptions.Singleline);

            Assert.True(section.Success, $"{requirement.Id} has no highlighted quote on the review page.");
            Assert.Equal(
                TextNormalizer.Normalize(requirement.Text),
                TextNormalizer.Normalize(WebUtility.HtmlDecode(section.Groups["quote"].Value)));
        }
    }

    [Fact]
    public async Task WithoutAReviewerNameThePageOffersNoDecisionButtons()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        await using var web = await WebApp.StartAsync(ReviewWorkspace.Host(workspace));

        var anonymous = await web.GetStringAsync(ReviewPage);

        Assert.DoesNotContain("value=\"accept\"", anonymous, StringComparison.Ordinal);
        Assert.Contains("Enter your name to decide", anonymous, StringComparison.Ordinal);

        await web.SetReviewerAsync(Workspace.ReferenceKey, ReviewWorkspace.Reviewer);

        var named = await web.GetStringAsync(ReviewPage);

        Assert.Contains("value=\"accept\"", named, StringComparison.Ordinal);
        Assert.Contains("self-declared", named, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownRunIsNotFound()
    {
        using var scratch = new ScratchDirectory();
        await using var web = await WebApp.StartAsync(ReviewWorkspace.Host(ReviewWorkspace.In(scratch)));

        foreach (var path in new[] { "/runs/rfc6902-000000000000", "/runs/rfc6902-000000000000/review", "/runs/../review", "/runs/reference-x/matrix" })
        {
            using var response = await web.Client.GetAsync(path);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    internal static async Task<Dictionary<string, string>> StatusesOnTheMatrixPageAsync(WebApp web, string key = Workspace.ReferenceKey)
    {
        var page = await web.GetStringAsync($"/runs/{key}/matrix");
        var rows = Regex.Matches(page, "data-requirement=\"(?<id>[^\"]+)\" data-status=\"(?<status>[^\"]+)\"");

        Assert.NotEmpty(rows);

        return rows.ToDictionary(row => row.Groups["id"].Value, row => row.Groups["status"].Value, StringComparer.Ordinal);
    }

    private static async Task PostAsync(WebApp web, string handler, KeyValuePair<string, string>[] fields)
    {
        using var response = await web.PostAsync(ReviewPage, handler, fields);

        Assert.True(
            response.StatusCode == HttpStatusCode.Redirect,
            $"{handler} {string.Join(", ", fields.Select(field => $"{field.Key}={field.Value}"))} answered "
            + $"{(int)response.StatusCode}: {Excerpt(await response.Content.ReadAsStringAsync())}");
    }

    private static string Excerpt(string page)
    {
        var error = Regex.Match(page, "<div class=\"error\"[^>]*>(?<text>.*?)</div>", RegexOptions.Singleline);

        return error.Success ? WebUtility.HtmlDecode(error.Groups["text"].Value) : page[..Math.Min(page.Length, 300)];
    }
}
