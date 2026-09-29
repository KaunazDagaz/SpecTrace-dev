using System.Text;
using SpecTrace.Pipeline;

namespace SpecTrace.Web;

public static class SpecTraceWebApp
{
    public const string ReviewerCookie = "spectrace-reviewer";

    public const string HealthPath = "/health";

    public static WebApplication Build(WebAppHost host, string[] args)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(args);

        CheckWorkspace(host.Workspace);

        if (host.PublicDemo && !host.Offline && !host.HasApiKey)
        {
            throw new InvalidOperationException(
                $"The public demo ({WebAppHost.PublicDemoVariable}=1 or {Program.PublicDemoFlag}) runs either offline or live with a key, "
                + $"and its banner says which. Live with no key, every run would fail: set {PipelineLaunch.ApiKeyVariable}, or set "
                + $"{PipelineLaunch.OfflineVariable}=1 or pass {Program.OfflineFlag}.");
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(SpecTraceWebApp).Assembly.GetName().Name,
        });

        builder.Services.AddSingleton(host);
        builder.Services.AddSingleton(new RunCatalog(host.Workspace, host.Time) { ReferenceReadOnly = host.PublicDemo });
        builder.Services.AddHealthChecks();
        builder.Services.AddSingleton(services => new RunCoordinator(
            host.Workspace,
            host.ModelClients,
            host.Prompts,
            LlmClientFactory.DefaultModel,
            host.Offline,
            host.Time,
            services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
        builder.Services.AddHostedService<RunLifetime>();
        builder.Services.AddRazorPages();

        var app = builder.Build();

        app.MapRazorPages();
        app.MapHealthChecks(HealthPath);
        app.MapGet("/runs/{key}/matrix.md", (string key, RunCatalog catalog, CancellationToken token) =>
            ExportAsync(key, MatrixExport.MarkdownFormat, "text/markdown", "md", catalog, token));
        app.MapGet("/runs/{key}/matrix.csv", (string key, RunCatalog catalog, CancellationToken token) =>
            ExportAsync(key, MatrixExport.CsvFormat, "text/csv", "csv", catalog, token));

        return app;
    }

    private static async Task<IResult> ExportAsync(
        string key,
        string format,
        string contentType,
        string extension,
        RunCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (!Workspace.IsRunKey(key))
        {
            return Results.NotFound();
        }

        try
        {
            var run = await catalog.LoadAsync(key, cancellationToken).ConfigureAwait(false);

            return Results.File(
                Encoding.UTF8.GetBytes(MatrixExport.Render(run, format)),
                $"{contentType}; charset=utf-8",
                $"{key}.matrix.{extension}");
        }
        catch (RunNotAvailableException exception)
        {
            return Results.Text(exception.Message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
        }
    }

    private static void CheckWorkspace(Workspace workspace)
    {
        foreach (var (name, path) in new[] { ("corpus", workspace.Corpus), ("reference run", workspace.Reference) })
        {
            if (!Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"The {name} directory '{Path.GetFullPath(path)}' does not exist. Start the web UI from the "
                    + "repository root, where corpus/ and runs/reference/ are.");
            }
        }

        var runs = FullDirectory(workspace.Runs);

        foreach (var (name, path) in new[] { ("reference run", workspace.Reference), ("committed cache", workspace.Cache), ("corpus", workspace.Corpus) })
        {
            var protectedDirectory = FullDirectory(path);

            if (runs.StartsWith(protectedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The runs directory '{runs}' lies inside the {name} '{protectedDirectory}'. The web UI never writes there.");
            }
        }
    }

    private static string FullDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
}
