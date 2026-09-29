using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SpecTrace.Llm;
using SpecTrace.Web;

namespace SpecTrace.Pipeline.Tests;

public sealed class WebRunTests
{
    private const string ReferenceRunId = "rfc6902-3ff2234db6aa";

    private const string ScriptQuote = "A conforming reviewer MUST NOT execute <script>alert(1)</script> found in a specification.";

    private static readonly IReadOnlyDictionary<string, string> Offline =
        new Dictionary<string, string>(StringComparer.Ordinal) { [PipelineLaunch.OfflineVariable] = "1" };

    private static readonly IReadOnlyDictionary<string, string> Live =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly byte[] NotInTheCache = Encoding.UTF8.GetBytes(
        "Network Working Group                                         Test\n"
        + "\n"
        + "1.  Introduction\n"
        + "\n"
        + "   A conforming reviewer MUST NOT execute <script>alert(1)</script>\n"
        + "   found in a specification.\n"
        + "\n"
        + "2.  Other\n"
        + "\n"
        + "   Nothing else is required.\n");

    [Fact]
    public async Task UploadingTheRfc6902CorpusFileOfflineUnderAnotherNameReproducesTheReferenceRun()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var network = new NoNetworkHandler();
        using var http = new HttpClient(network);
        await using var web = await WebApp.StartAsync(OfflineHost(workspace, http));

        using var response = await UploadAsync(web, "my copy of the JSON patch spec.txt", await File.ReadAllBytesAsync(Repository.PathTo("corpus", "rfc6902.txt")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/runs/{ReferenceRunId}", response.Headers.Location?.OriginalString);

        var overview = await web.GetStringAsync($"/runs/{ReferenceRunId}");

        Assert.Contains("Review this run", overview, StringComparison.Ordinal);
        Assert.Contains("Quote verification rate: 77.8 %", overview, StringComparison.Ordinal);

        RunComparison.AssertSameArtifacts(Repository.PathTo("runs", "reference"), workspace.RunDirectory(ReferenceRunId));

        var record = new RunRecords(workspace).Read(ReferenceRunId)!;

        Assert.Equal(RunState.Completed, record.State);
        Assert.Equal("rfc6902", record.DocumentId);
        Assert.Equal(DocumentSource.Corpus, record.Source);
        Assert.Equal(new VerificationFigures(18, 14, 4, 0), record.Figures);
        Assert.Empty(network.Attempted);
        Assert.False(Directory.Exists(workspace.UploadsDirectory), "A corpus document was copied as an upload.");
        Assert.False(Directory.Exists(workspace.UploadCache));
    }

    [Fact]
    public async Task UploadingADocumentNotInTheCacheOfflineEndsInAFailedRunWithTheCacheMissReasonAndOffersNothingForReview()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var network = new NoNetworkHandler();
        using var http = new HttpClient(network);
        await using var web = await WebApp.StartAsync(OfflineHost(workspace, http));

        using var response = await UploadAsync(web, "notes.txt", NotInTheCache);
        var location = response.Headers.Location?.OriginalString;

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/runs/notes-", location, StringComparison.Ordinal);

        var runId = location!["/runs/".Length..];
        var overview = WebUtility.HtmlDecode(await web.GetStringAsync(location));

        Assert.Contains("This run failed.", overview, StringComparison.Ordinal);
        Assert.Contains("This document is not in the cache, and the server runs offline", overview, StringComparison.Ordinal);
        Assert.Contains("a live run needs GEMINI_API_KEY", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("Review this run", overview, StringComparison.Ordinal);

        foreach (var path in new[] { $"/runs/{runId}/review", $"/runs/{runId}/matrix", $"/runs/{runId}/matrix.csv", $"/runs/{runId}/matrix.md" })
        {
            using var page = await web.Client.GetAsync(path);

            Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        }

        var list = await web.GetStringAsync("/");

        Assert.DoesNotContain($"/runs/{runId}/review", list, StringComparison.Ordinal);
        Assert.Contains("not offered for review", list, StringComparison.Ordinal);
        Assert.Equal(RunState.Failed, new RunRecords(workspace).Read(runId)!.State);
        Assert.False(Directory.Exists(workspace.RunDirectory(runId)));
        Assert.Empty(network.Attempted);
    }

    [Fact]
    public async Task ALiveRunOfAnUploadWithAFakeProviderWritesItsCopyRunAndCacheOnlyToGitignoredLocations()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var protectedDirectories = new[] { Repository.PathTo("cache"), Repository.PathTo("corpus"), Repository.PathTo("runs", "reference") };
        var before = protectedDirectories.Select(RunComparison.Snapshot).ToList();
        var provider = FakeProvider();

        await using (var web = await WebApp.StartAsync(ReviewWorkspace.Host(
            workspace,
            Live,
            (cache, offline) => LlmClientFactory.Create(provider, cache, offline))))
        {
            using var response = await UploadAsync(web, "..\\..\\cache\\Notes on Review.TXT", NotInTheCache);
            var location = response.Headers.Location!.OriginalString;
            var runId = location["/runs/".Length..];

            Assert.StartsWith("notes-on-review-", runId, StringComparison.Ordinal);
            Assert.Contains("Review this run", await web.GetStringAsync(location), StringComparison.Ordinal);

            var review = await web.GetStringAsync($"{location}/review");

            Assert.Matches("<mark>[^<]*&lt;script&gt;alert\\(1\\)&lt;/script&gt;[^<]*</mark>", review);
            Assert.DoesNotContain("<script>alert(1)", review, StringComparison.Ordinal);
        }

        Assert.Equal(2, provider.Requests.Count);
        Assert.Equal(before, protectedDirectories.Select(RunComparison.Snapshot).ToList());

        var written = Directory.EnumerateFiles(workspace.Runs, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(workspace.Runs, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        var copy = Assert.Single(written, path => path.StartsWith("web/uploads/", StringComparison.Ordinal));

        Assert.EndsWith("/notes-on-review.txt", copy, StringComparison.Ordinal);
        Assert.Equal(NotInTheCache, await File.ReadAllBytesAsync(Path.Combine(workspace.Runs, copy)));
        Assert.Equal(2, written.Count(path => path.StartsWith("web/cache/", StringComparison.Ordinal)));
        Assert.Single(written, path => path.StartsWith("web/status/", StringComparison.Ordinal));
        Assert.Equal(RunReader.PipelineFiles.Count, written.Count(path => path.StartsWith("notes-on-review-", StringComparison.Ordinal)));
        Assert.Equal(written.Count, 1 + 2 + 1 + RunReader.PipelineFiles.Count);
        Assert.Equal([workspace.Reference, workspace.Runs], Directory.EnumerateDirectories(scratch.Path).Order(StringComparer.Ordinal));

        foreach (var path in written)
        {
            var (ignored, _) = Git("check-ignore", "--no-index", "-q", $"runs/{path}");
            var (_, source) = Git("check-ignore", "--no-index", "-v", $"runs/{path}");

            Assert.True(ignored, $"runs/{path} is not ignored by git.");
            Assert.StartsWith(".gitignore:", source, StringComparison.Ordinal);
            Assert.DoesNotContain(":!", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ReviewActionsAndEveryPageMakeNoCallIntoTheLlmLayerNotEvenACachedOne()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        using var http = new HttpClient(new NoNetworkHandler());
        var counter = new CallCounter();
        var production = PipelineLaunch.ModelClients(http, name => Offline.GetValueOrDefault(name));

        await using var web = await WebApp.StartAsync(ReviewWorkspace.Host(workspace, Offline, counter.Wrap(production)));

        foreach (var path in new[] { "/", "/runs/reference", "/runs/reference/review", "/runs/reference/matrix", "/runs/reference/matrix.md", "/runs/reference/matrix.csv" })
        {
            using var response = await web.Client.GetAsync(path);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await web.SetReviewerAsync(Workspace.ReferenceKey, ReviewWorkspace.Reviewer);

        foreach (var (handler, fields) in new (string, KeyValuePair<string, string>[])[]
        {
            ("Case", ReviewWorkspace.Case("TC-07e6ff-01", "accept")),
            ("Case", ReviewWorkspace.Edit("TC-07e6ff-02", "A sharper title")),
            ("Case", ReviewWorkspace.Case("TC-07e6ff-03", "reject")),
            ("Item", ReviewWorkspace.Item("DQ-rfc6902-5a829e", "testable")),
            ("Item", ReviewWorkspace.Item("DQ-rfc6902-5a829e", "not_testable")),
            ("Item", ReviewWorkspace.Item("DQ-rfc6902-5a829e", "defer")),
        })
        {
            using var response = await web.PostAsync("/runs/reference/review", handler, fields);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        Assert.Equal(0, counter.Clients);
        Assert.Equal(0, counter.Calls);

        using var upload = await UploadAsync(web, "rfc6902.txt", await File.ReadAllBytesAsync(Repository.PathTo("corpus", "rfc6902.txt")));

        Assert.Equal(HttpStatusCode.Redirect, upload.StatusCode);
        Assert.Equal(1, counter.Clients);
        Assert.Equal(13, counter.Calls);
    }

    [Fact]
    public async Task ASecondSubmissionWhileARunIsInProgressGetsAClearMessageAndTheOverviewShowsTheRunInProgress()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new GatedLlmClient(FakeProvider(), gate.Task);

        await using var web = await WebApp.StartAsync(ReviewWorkspace.Host(
            workspace,
            Live,
            (cache, offline) => LlmClientFactory.Create(provider, cache, offline),
            runWait: TimeSpan.FromMilliseconds(200)));

        using var first = await UploadAsync(web, "notes.txt", NotInTheCache);
        var location = first.Headers.Location!.OriginalString;
        var inProgress = await web.GetStringAsync(location);

        Assert.Contains("This run is still in progress.", inProgress, StringComparison.Ordinal);
        Assert.Contains("<meta http-equiv=\"refresh\" content=\"5\">", inProgress, StringComparison.Ordinal);
        Assert.DoesNotContain("Review this run", inProgress, StringComparison.Ordinal);

        using var second = await UploadAsync(web, "other.txt", Encoding.UTF8.GetBytes("Another document MUST wait.\n"));
        var refusal = WebUtility.HtmlDecode(await second.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("A run is already in progress: notes", refusal, StringComparison.Ordinal);
        Assert.Contains("Runs are not queued", refusal, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(workspace.UploadsDirectory, "other.txt", SearchOption.AllDirectories));

        gate.SetResult();
        await web.Services.GetRequiredService<RunCoordinator>().Current.WaitAsync(TimeSpan.FromMinutes(1));

        Assert.Contains("Review this run", await web.GetStringAsync(location), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARunLeftRunningByAStoppedServerIsShownAsInterruptedWithItsReasonAndNeverOfferedForReview()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        const string RunId = "notes-000000000000";

        new RunRecords(workspace).Write(new RunRecord(
            RunId, "notes", DocumentSource.Upload, new string('0', 64), false, RunState.Running, null, DateTimeOffset.UtcNow, null, null));

        await using var web = await WebApp.StartAsync(ReviewWorkspace.Host(workspace, Live));

        var overview = WebUtility.HtmlDecode(await web.GetStringAsync($"/runs/{RunId}"));
        using var review = await web.Client.GetAsync($"/runs/{RunId}/review");

        Assert.Contains("This run interrupted.", overview, StringComparison.Ordinal);
        Assert.Contains("The server stopped before this run finished", overview, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, review.StatusCode);
        Assert.Equal(RunState.Interrupted, new RunRecords(workspace).Read(RunId)!.State);
    }

    [Fact]
    public async Task AnExhaustedDailyQuotaIsReportedAsSuch()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var provider = new RoutingLlmClient(_ => throw new LlmQuotaExhaustedException(
            "Quota exceeded.", "GenerateRequestsPerDayPerProjectPerModel-FreeTier", "500"));

        await using var web = await WebApp.StartAsync(ReviewWorkspace.Host(
            workspace,
            Live,
            (cache, offline) => LlmClientFactory.Create(provider, cache, offline)));

        using var response = await UploadAsync(web, "notes.txt", NotInTheCache);
        var overview = WebUtility.HtmlDecode(await web.GetStringAsync(response.Headers.Location!.OriginalString));

        Assert.Contains("This run failed.", overview, StringComparison.Ordinal);
        Assert.Contains("daily request quota is exhausted (quota GenerateRequestsPerDayPerProjectPerModel-FreeTier, limit 500)", overview, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("oversize")]
    [InlineData("latin1")]
    [InlineData("nul")]
    public async Task AnUploadThatIsNotUsablePlainTextIsRefusedBeforeAnythingIsWritten(string kind)
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var counter = new CallCounter();

        await using var web = await WebApp.StartAsync(ReviewWorkspace.Host(workspace, Live, counter.Wrap((_, _) => new RoutingLlmClient(_ => "[]"))));

        var (bytes, reason) = kind switch
        {
            "empty" => (Array.Empty<byte>(), "The file is empty."),
            "oversize" => (Enumerable.Repeat((byte)'a', DocumentIntake.MaxBytes + 1).ToArray(), "the limit is 65,536 bytes"),
            "latin1" => (new byte[] { 0x53, 0x70, 0x65, 0x63, 0xE9, 0x0A }, "not UTF-8 plain text"),
            "nul" => (new byte[] { 0x41, 0x00, 0x42 }, "NUL character"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        using var response = await UploadAsync(web, "spec.txt", bytes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.False(Directory.Exists(workspace.Runs));
        Assert.Equal(0, counter.Clients);
    }

    [Fact]
    public async Task TheFormStatesWhatThePipelineIsBuiltForAndTheLimitAndOfflineSaysOnlyCachedDocumentsCanRun()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        using var http = new HttpClient(new NoNetworkHandler());

        await using var offline = await WebApp.StartAsync(OfflineHost(workspace, http));
        var offlinePage = WebUtility.HtmlDecode(await offline.GetStringAsync("/"));

        Assert.Contains("IETF RFC plain text", offlinePage, StringComparison.Ordinal);
        Assert.Contains("public specifications only", offlinePage, StringComparison.Ordinal);
        Assert.Contains("65,536 bytes (64 KiB)", offlinePage, StringComparison.Ordinal);
        Assert.Contains("This server runs offline.", offlinePage, StringComparison.Ordinal);
        Assert.Contains("only a document whose calls are in the cache can run", offlinePage, StringComparison.Ordinal);
        Assert.Contains("rfc6902.txt", offlinePage, StringComparison.Ordinal);

        using var liveScratch = new ScratchDirectory();
        await using var live = await WebApp.StartAsync(ReviewWorkspace.Host(ReviewWorkspace.In(liveScratch), Live));
        var livePage = WebUtility.HtmlDecode(await live.GetStringAsync("/"));

        Assert.Contains("This server runs live.", livePage, StringComparison.Ordinal);
        Assert.Contains("No API key is set", livePage, StringComparison.Ordinal);
        Assert.DoesNotContain("This server runs offline.", livePage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveWithNoKeyEvenAFullyCachedCorpusDocumentFailsWithTheMissingKeyReasonAndThePageSaysSo()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        var network = new NoNetworkHandler();
        using var http = new HttpClient(network);
        await using var web = await WebApp.StartAsync(ReviewWorkspace.Host(workspace, Live, PipelineLaunch.ModelClients(http, name => Live.GetValueOrDefault(name))));

        var form = WebUtility.HtmlDecode(await web.GetStringAsync("/"));

        Assert.Contains("in the server's environment, so no run can start here", form, StringComparison.Ordinal);

        using var response = await web.PostAsync("/", "Corpus", [new("corpus", "rfc6902.txt")]);
        await web.Services.GetRequiredService<RunCoordinator>().Current;

        var record = new RunRecords(workspace).Read(ReferenceRunId)!;

        Assert.Equal(RunState.Failed, record.State);
        Assert.Contains($"{PipelineLaunch.ApiKeyVariable} is not set", record.Reason, StringComparison.Ordinal);
        Assert.Empty(network.Attempted);
    }

    private static WebAppHost OfflineHost(Workspace workspace, HttpClient http) =>
        ReviewWorkspace.Host(workspace, Offline, PipelineLaunch.ModelClients(http, name => Offline.GetValueOrDefault(name)));

    private static RoutingLlmClient FakeProvider() =>
        RoutingLlmClient.For(
            ModelAnswer.With(("MUST_NOT", ScriptQuote, "testable")),
            _ => GenerationAnswerJson.Cases(("negative", "A script in the specification is not executed")));

    private static async Task<HttpResponseMessage> UploadAsync(WebApp web, string fileName, byte[] bytes)
    {
        var token = await web.AntiforgeryTokenAsync("/");
        var file = new ByteArrayContent(bytes);

        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { file, "document", fileName },
        };

        return await web.Client.PostAsync("/", form);
    }

    private static (bool Succeeded, string Output) Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = Repository.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var git = Process.Start(start)!;
        var output = git.StandardOutput.ReadToEnd();

        git.WaitForExit();

        return (git.ExitCode == 0, output.Trim());
    }

    private sealed class CallCounter
    {
        private int _clients;
        private int _calls;

        public int Clients => _clients;

        public int Calls => _calls;

        public ModelClientFactory Wrap(ModelClientFactory inner) => (cache, offline) =>
        {
            Interlocked.Increment(ref _clients);

            return new CountingClient(inner(cache, offline), this);
        };

        private sealed class CountingClient(ILlmClient inner, CallCounter counter) : ILlmClient
        {
            public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref counter._calls);

                return inner.CompleteAsync(request, cancellationToken);
            }
        }
    }

    private sealed class GatedLlmClient(ILlmClient inner, Task gate) : ILlmClient
    {
        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);

            return await inner.CompleteAsync(request, cancellationToken);
        }
    }
}
