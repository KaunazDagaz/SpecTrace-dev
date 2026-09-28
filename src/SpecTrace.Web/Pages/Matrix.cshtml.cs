using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SpecTrace.Pipeline;

namespace SpecTrace.Web.Pages;

public sealed class MatrixModel : PageModel
{
    private readonly RunCatalog _catalog;

    public MatrixModel(RunCatalog catalog) => _catalog = catalog;

    public LoadedRun Run { get; private set; } = null!;

    public IReadOnlyList<ReviewedRow> Rows { get; private set; } = [];

    public string? Unavailable { get; private set; }

    public async Task<IActionResult> OnGetAsync(string key, CancellationToken cancellationToken)
    {
        if (!Workspace.IsRunKey(key) || _catalog.Find(key) is null)
        {
            return NotFound();
        }

        try
        {
            Run = await _catalog.LoadAsync(key, cancellationToken);
            Rows = MatrixExport.RowsOf(Run);
        }
        catch (RunNotAvailableException exception)
        {
            Unavailable = exception.Message;
            Response.StatusCode = StatusCodes.Status404NotFound;
        }

        return Page();
    }
}
