using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SpecTrace.Pipeline;

namespace SpecTrace.Web.Pages;

public sealed class RunModel : PageModel
{
    public const int RefreshSeconds = 5;

    private readonly RunCatalog _catalog;

    public RunModel(RunCatalog catalog) => _catalog = catalog;

    public RunListing Listing { get; private set; } = null!;

    public LoadedRun? Run { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(string key, CancellationToken cancellationToken)
    {
        if (!Workspace.IsRunKey(key) || _catalog.Find(key) is not { } listing)
        {
            return NotFound();
        }

        Listing = listing;

        if (listing.State == RunState.Running)
        {
            ViewData["Refresh"] = RefreshSeconds;
            return Page();
        }

        if (listing.IsReviewable)
        {
            try
            {
                Run = await _catalog.LoadAsync(key, cancellationToken);
            }
            catch (RunNotAvailableException exception)
            {
                Error = exception.Message;
            }
        }

        return Page();
    }
}
