using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SpecTrace.Pipeline;

namespace SpecTrace.Web.Pages;

[RequestSizeLimit(RequestLimit)]
[RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
public sealed class IndexModel : PageModel
{
    public const int RequestLimit = 1024 * 1024;

    private readonly RunCatalog _catalog;
    private readonly RunCoordinator _coordinator;

    public IndexModel(RunCatalog catalog, RunCoordinator coordinator, WebAppHost host)
    {
        _catalog = catalog;
        _coordinator = coordinator;
        Host = host;
    }

    public WebAppHost Host { get; }

    public IReadOnlyList<RunListing> Runs { get; private set; } = [];

    public RunRecord? InProgress => _coordinator.Running;

    public IReadOnlyList<string> CorpusDocuments { get; private set; } = [];

    public string? Refusal { get; private set; }

    public void OnGet() => Load();

    public async Task<IActionResult> OnPostAsync(IFormFile? document, CancellationToken cancellationToken)
    {
        if (document is null)
        {
            return Refuse("Choose a plain-text file to upload.", StatusCodes.Status400BadRequest);
        }

        if (DocumentIntake.SizeProblem(document.Length) is { } problem)
        {
            return Refuse(problem, StatusCodes.Status400BadRequest);
        }

        byte[] bytes;

        using (var buffer = new MemoryStream())
        {
            await using var stream = document.OpenReadStream();

            await stream.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        var outcome = await _coordinator.StartAsync(document.FileName, bytes, cancellationToken);

        switch (outcome)
        {
            case StartOutcome.Refused refused:
                return Refuse(refused.Reason, StatusCodes.Status400BadRequest);

            case StartOutcome.Busy busy:
                return Refuse(
                    "A run is already in progress"
                    + (busy.Current is { } current ? $": {current.DocumentId}, run {current.RunId}, started {Display.Utc(current.StartedAt)}" : string.Empty)
                    + ". Runs are not queued: wait for it to finish, then submit again.",
                    StatusCodes.Status409Conflict);

            case StartOutcome.AlreadyRun already:
                return Redirect($"/runs/{already.RunId}");

            case StartOutcome.Started started:
                await Task.WhenAny(started.Completion, Task.Delay(Host.RunWait, cancellationToken));
                return Redirect($"/runs/{started.RunId}");

            default:
                throw new InvalidOperationException($"Unknown start outcome {outcome.GetType().Name}.");
        }
    }

    private IActionResult Refuse(string reason, int statusCode)
    {
        Load();
        Refusal = reason;
        Response.StatusCode = statusCode;

        return Page();
    }

    private void Load()
    {
        Runs = _catalog.List();
        CorpusDocuments = Directory.Exists(Host.Workspace.Corpus)
            ? Directory.EnumerateFiles(Host.Workspace.Corpus, "*.txt")
                .Select(path => Path.GetFileName(path))
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];
    }
}
