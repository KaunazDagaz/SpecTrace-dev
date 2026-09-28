using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SpecTrace.Web;

namespace SpecTrace.Pipeline.Tests;

internal sealed partial class WebApp : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClientHandler _handler;

    private WebApp(WebApplication app, HttpClientHandler handler, HttpClient client)
    {
        _app = app;
        _handler = handler;
        Client = client;
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<WebApp> StartAsync(WebAppHost host)
    {
        var app = SpecTraceWebApp.Build(
            host,
            ["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Warning"]);

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
        var client = new HttpClient(handler) { BaseAddress = new Uri(address), Timeout = TimeSpan.FromMinutes(2) };

        return new WebApp(app, handler, client);
    }

    public async Task<string> GetStringAsync(string path)
    {
        using var response = await Client.GetAsync(path);

        return await response.Content.ReadAsStringAsync();
    }

    public Task<HttpResponseMessage> PostAsync(string pagePath, string handler, IEnumerable<KeyValuePair<string, string>> fields) =>
        PostAsync(pagePath, handler, fields, tokenPage: pagePath);

    public async Task<HttpResponseMessage> PostAsync(
        string pagePath,
        string handler,
        IEnumerable<KeyValuePair<string, string>> fields,
        string tokenPage)
    {
        var token = await AntiforgeryTokenAsync(tokenPage);
        var form = fields.Append(new KeyValuePair<string, string>("__RequestVerificationToken", token)).ToList();

        return await Client.PostAsync($"{pagePath}?handler={handler}", new FormUrlEncodedContent(form));
    }

    public async Task SetReviewerAsync(string key, string name)
    {
        using var response = await PostAsync($"/runs/{key}/review", "Reviewer", [new("name", name)]);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    public async Task<string> AntiforgeryTokenAsync(string pagePath)
    {
        var page = await GetStringAsync(pagePath);
        var match = TokenPattern().Match(page);

        Assert.True(match.Success, $"{pagePath} holds no antiforgery token.");

        return WebUtility.HtmlDecode(match.Groups["token"].Value);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        _handler.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex TokenPattern();
}

internal static class ReviewWorkspace
{
    public const string Reviewer = "Test Reviewer";

    public static Workspace In(ScratchDirectory scratch)
    {
        var reference = Path.Combine(scratch.Path, "reference");

        CopyDirectory(Repository.PathTo("runs", "reference"), reference);

        return new Workspace(
            Repository.PathTo("corpus"),
            Repository.PathTo("cache"),
            Path.Combine(scratch.Path, "runs"),
            reference,
            Repository.PathTo("experiments"));
    }

    public static WebAppHost Host(
        Workspace workspace,
        IReadOnlyDictionary<string, string>? environment = null,
        ModelClientFactory? modelClients = null,
        TimeSpan? runWait = null) =>
        new(
            workspace,
            name => environment?.GetValueOrDefault(name),
            OfflineFlag: false,
            TimeProvider.System,
            modelClients ?? ((_, _) => throw new InvalidOperationException("This test never calls the model.")),
            PromptSet.Embedded,
            runWait ?? WebAppHost.DefaultRunWait);

    public static string LogOf(Workspace workspace, string key = Workspace.ReferenceKey) => workspace.ReviewLog(key);

    public static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }

    public static void EditTestCases(string runDirectory, Action<JsonArray> edit)
    {
        var path = Path.Combine(runDirectory, RunArtifacts.TestCasesFile);
        var cases = JsonNode.Parse(File.ReadAllText(path))!.AsArray();

        edit(cases);
        File.WriteAllText(path, cases.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static KeyValuePair<string, string>[] Case(string caseId, string decision, string author = Reviewer) =>
        [new("caseId", caseId), new("decision", decision), new("author", author)];

    public static KeyValuePair<string, string>[] Edit(
        string caseId,
        string title,
        string type = "negative",
        string expectedResult = "The patch is rejected and the target is unchanged.",
        string author = Reviewer) =>
    [
        new("caseId", caseId),
        new("decision", "edit"),
        new("author", author),
        new("title", title),
        new("type", type),
        new("precondition", "A target JSON document."),
        new("input", "A patch."),
        new("expectedResult", expectedResult),
    ];

    public static KeyValuePair<string, string>[] Item(string itemId, string decision, string author = Reviewer) =>
        [new("itemId", itemId), new("decision", decision), new("author", author)];
}
