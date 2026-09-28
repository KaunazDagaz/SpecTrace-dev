using Microsoft.AspNetCore.Mvc.RazorPages;
using SpecTrace.Pipeline;

namespace SpecTrace.Web.Pages;

public sealed class IndexModel : PageModel
{
    private readonly RunCatalog _catalog;

    public IndexModel(RunCatalog catalog, WebAppHost host)
    {
        _catalog = catalog;
        Host = host;
    }

    public WebAppHost Host { get; }

    public IReadOnlyList<RunListing> Runs { get; private set; } = [];

    public void OnGet() => Runs = _catalog.List();
}
