using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SpecTrace.Core;
using SpecTrace.Web;

namespace SpecTrace.Pipeline.Tests;

public sealed partial class WebDemoTests
{
    private const string ReferenceRunId = "rfc6902-3ff2234db6aa";

    private const string ReviewPage = "/runs/reference/review";

    private const string Author = "The Author";

    private static readonly IReadOnlyDictionary<string, string> Demo =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PipelineLaunch.OfflineVariable] = "1",
            [WebAppHost.PublicDemoVariable] = "1",
        };

    private static readonly IReadOnlyDictionary<string, string> OfflineOnly =
        new Dictionary<string, string>(StringComparer.Ordinal) { [PipelineLaunch.OfflineVariable] = "1" };

    [Fact]
    public async Task OnThePublicDemoEveryDecisionOnTheReferenceRunIsRefusedWith403AndTheLogStaysByteIdentical()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var log = await SeedReferenceLogAsync(workspace);
        var before = await File.ReadAllBytesAsync(log);
        using var http = new HttpClient(new NoNetworkHandler());
        await using var web = await WebApp.StartAsync(Host(workspace, Demo, http));

        (string Handler, KeyValuePair<string, string>[] Fields)[] attempts =
        [
            ("Case", ReviewWorkspace.Case("TC-07e6ff-02", "reject")),
            ("Case", ReviewWorkspace.Case("TC-07e6ff-01", "accept")),
            ("Case", ReviewWorkspace.Edit("TC-07e6ff-02", "A title nobody may log here")),
            ("Item", ReviewWorkspace.Item("DQ-rfc6902-5a829e", "not_testable")),
            ("Item", ReviewWorkspace.Item("DQ-rfc6902-1dd2c2", "defer")),
            ("Case", [new("caseId", "TC-07e6ff-02"), new("decision", "reject")]),
        ];

        foreach (var (handler, fields) in attempts)
        {
            using var response = await web.PostAsync(ReviewPage, handler, fields, tokenPage: "/");
            var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains(RunCatalog.ReadOnlyReason, page, StringComparison.Ordinal);
        }

        Assert.Equal(before, await File.ReadAllBytesAsync(log));
    }

    [Fact]
    public async Task AReadOnlyReferenceIsRefusedByTheCatalogItselfBeforeAnythingIsReadOrWritten()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var catalog = new RunCatalog(workspace, TimeProvider.System) { ReferenceReadOnly = true };

        await Assert.ThrowsAsync<RunReadOnlyException>(() =>
            catalog.RecordCaseDecisionAsync(Workspace.ReferenceKey, "TC-07e6ff-01", CaseDecision.Accept, null, Author, CancellationToken.None));
        await Assert.ThrowsAsync<RunReadOnlyException>(() =>
            catalog.RecordQueueDecisionAsync(Workspace.ReferenceKey, "DQ-rfc6902-5a829e", QueueDecision.Defer, Author, CancellationToken.None));

        Assert.False(File.Exists(ReviewWorkspace.LogOf(workspace)));
        Assert.False(new RunCatalog(workspace, TimeProvider.System).IsReadOnly(Workspace.ReferenceKey));
        Assert.False(catalog.IsReadOnly(ReferenceRunId));
    }

    [Fact]
    public async Task TheReadOnlyReferenceReviewPageShowsTheLoggedDecisionsAndOffersNoDecisionFormEvenWithAName()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        await SeedReferenceLogAsync(workspace);
        using var http = new HttpClient(new NoNetworkHandler());
        await using var web = await WebApp.StartAsync(Host(workspace, Demo, http));

        using (var named = await web.PostAsync(ReviewPage, "Reviewer", [new("name", ReviewWorkspace.Reviewer)], tokenPage: "/"))
        {
            Assert.Equal(HttpStatusCode.Redirect, named.StatusCode);
        }

        var page = WebUtility.HtmlDecode(await web.GetStringAsync(ReviewPage));

        Assert.Contains("id=\"read-only\"", page, StringComparison.Ordinal);
        Assert.Contains("This run is read-only on this server.", page, StringComparison.Ordinal);
        Assert.Contains($"accept by {Author} (self-declared)", page, StringComparison.Ordinal);
        Assert.Contains("Lines in the review log: 1.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"decision\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"reviewer-name\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnThePublicDemoAVisitorsOwnRunFromTheCorpusChoiceAcceptsDecisionsInItsOwnLog()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        using var http = new HttpClient(new NoNetworkHandler());
        await using var web = await WebApp.StartAsync(Host(workspace, Demo, http));

        using (var started = await web.PostAsync("/", "Corpus", [new("corpus", "rfc6902.txt")]))
        {
            Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
            Assert.Equal($"/runs/{ReferenceRunId}", started.Headers.Location?.OriginalString);
        }

        await web.SetReviewerAsync(ReferenceRunId, ReviewWorkspace.Reviewer);

        var page = await web.GetStringAsync($"/runs/{ReferenceRunId}/review");

        Assert.Contains("value=\"accept\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"read-only\"", page, StringComparison.Ordinal);

        using (var decided = await web.PostAsync($"/runs/{ReferenceRunId}/review", "Case", ReviewWorkspace.Case("TC-07e6ff-01", "accept")))
        {
            Assert.Equal(HttpStatusCode.Redirect, decided.StatusCode);
        }

        var logged = ReviewLog.Parse(workspace.ReviewLog(ReferenceRunId), await File.ReadAllBytesAsync(workspace.ReviewLog(ReferenceRunId)));

        Assert.Equal("TC-07e6ff-01", Assert.IsType<ReviewRecord>(Assert.Single(logged)).TestCaseId);
        Assert.False(File.Exists(ReviewWorkspace.LogOf(workspace)));
    }

    [Fact]
    public async Task ChoosingRfc6902FromTheCorpusChoiceReproducesTheReferenceRunWithoutANetworkRequest()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var network = new NoNetworkHandler();
        using var http = new HttpClient(network);
        await using var web = await WebApp.StartAsync(Host(workspace, OfflineOnly, http));

        using var response = await web.PostAsync("/", "Corpus", [new("corpus", "rfc6902.txt")]);
        await web.Services.GetRequiredService<RunCoordinator>().Current;

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/runs/{ReferenceRunId}", response.Headers.Location?.OriginalString);
        RunComparison.AssertSameArtifacts(Repository.PathTo("runs", "reference"), workspace.RunDirectory(ReferenceRunId));
        Assert.Equal(DocumentSource.Corpus, new RunRecords(workspace).Read(ReferenceRunId)!.Source);
        Assert.False(Directory.Exists(workspace.UploadsDirectory));
        Assert.Empty(network.Attempted);
    }

    [Fact]
    public async Task TheFormOffersEveryCorpusDocumentAndEachOneReplaysToCompletionOffline()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var network = new NoNetworkHandler();
        using var http = new HttpClient(network);
        await using var web = await WebApp.StartAsync(Host(workspace, Demo, http));

        var offered = OptionPattern().Matches(await web.GetStringAsync("/")).Select(match => match.Groups["name"].Value).ToList();
        var corpus = Directory.EnumerateFiles(Repository.PathTo("corpus"), "*.txt")
            .Select(path => Path.GetFileName(path))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(corpus, offered);
        Assert.Contains("rfc6902.txt", offered);
        Assert.Contains("rfc10050.txt", offered);

        foreach (var name in offered)
        {
            using var response = await web.PostAsync("/", "Corpus", [new("corpus", name)]);
            await web.Services.GetRequiredService<RunCoordinator>().Current;

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

            var runId = response.Headers.Location!.OriginalString["/runs/".Length..];
            var record = new RunRecords(workspace).Read(runId)!;

            Assert.True(record.State == RunState.Completed, $"{name}: {record.State} {record.Reason}");
            Assert.Equal(Path.GetFileNameWithoutExtension(name), record.DocumentId);

            foreach (var page in new[] { $"/runs/{runId}/review", $"/runs/{runId}/matrix" })
            {
                using var rendered = await web.Client.GetAsync(page);

                Assert.Equal(HttpStatusCode.OK, rendered.StatusCode);
            }
        }

        Assert.Empty(network.Attempted);
    }

    [Theory]
    [InlineData("../cache/rfc6902.txt")]
    [InlineData("missing.txt")]
    [InlineData("RFC6902.TXT")]
    [InlineData("rfc6902")]
    [InlineData("")]
    public async Task ACorpusChoiceThatIsNotAListedDocumentIsRefusedAndStartsNothing(string choice)
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        using var http = new HttpClient(new NoNetworkHandler());
        await using var web = await WebApp.StartAsync(Host(workspace, Demo, http));

        using var response = await web.PostAsync("/", "Corpus", [new("corpus", choice)]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Choose one of the corpus documents", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(workspace.Runs));
    }

    [Fact]
    public async Task TheBannerSaysWhatThePublicDemoIsOnEveryPageAndIsAbsentWithoutIt()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        using var http = new HttpClient(new NoNetworkHandler());

        await using (var demo = await WebApp.StartAsync(Host(workspace, Demo, http)))
        {
            foreach (var path in new[] { "/", "/runs/reference", ReviewPage, "/runs/reference/matrix" })
            {
                var page = WebUtility.HtmlDecode(await demo.GetStringAsync(path));

                Assert.Contains("id=\"demo\"", page, StringComparison.Ordinal);
                Assert.Contains("This is an offline demo.", page, StringComparison.Ordinal);
                Assert.Contains("never calls a model; there is no API key anywhere in this service", page, StringComparison.Ordinal);
                Assert.Contains("A new document needs a local run with an API key", page, StringComparison.Ordinal);
                Assert.Contains("Runs and decisions made here live only in this instance and disappear when it restarts", page, StringComparison.Ordinal);
                Assert.Contains("Reviewer names are self-declared", page, StringComparison.Ordinal);
            }
        }

        using var plainScratch = new ScratchDirectory();
        await using var plain = await WebApp.StartAsync(Host(ReviewWorkspace.In(plainScratch), OfflineOnly, http));

        Assert.DoesNotContain("id=\"demo\"", await plain.GetStringAsync("/"), StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"read-only\"", await plain.GetStringAsync(ReviewPage), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePublicDemoRefusesToStartUnlessTheServerRunsOffline()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var demoOnly = new Dictionary<string, string>(StringComparer.Ordinal) { [WebAppHost.PublicDemoVariable] = "1" };

        foreach (var host in new[]
        {
            ReviewWorkspace.Host(workspace, demoOnly),
            ReviewWorkspace.Host(workspace) with { PublicDemoFlag = true },
        })
        {
            var refusal = Assert.Throws<InvalidOperationException>(() => SpecTraceWebApp.Build(host, []));

            Assert.Contains(PipelineLaunch.OfflineVariable, refusal.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheHealthEndpointAnswers()
    {
        using var scratch = new ScratchDirectory();
        using var http = new HttpClient(new NoNetworkHandler());
        await using var web = await WebApp.StartAsync(Host(ReviewWorkspace.In(scratch), Demo, http));

        using var response = await web.Client.GetAsync(SpecTraceWebApp.HealthPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    private static WebAppHost Host(Workspace workspace, IReadOnlyDictionary<string, string> environment, HttpClient http) =>
        ReviewWorkspace.Host(workspace, environment, PipelineLaunch.ModelClients(http, name => environment.GetValueOrDefault(name)));

    private static async Task<string> SeedReferenceLogAsync(Workspace workspace)
    {
        var log = ReviewWorkspace.LogOf(workspace);
        var at = DateTimeOffset.Parse("2026-09-28T10:00:00Z", CultureInfo.InvariantCulture);

        await ReviewLog.AppendAsync(
            log,
            new ReviewRecord(ReferenceRunId, "TC-07e6ff-01", CaseDecision.Accept, null, Author, at),
            CancellationToken.None);

        return log;
    }

    [GeneratedRegex("<option value=\"(?<name>[^\"]+)\">")]
    private static partial Regex OptionPattern();
}
