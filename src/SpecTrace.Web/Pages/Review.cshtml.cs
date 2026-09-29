using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SpecTrace.Core;
using SpecTrace.Pipeline;

namespace SpecTrace.Web.Pages;

public sealed class ReviewModel : PageModel
{
    private readonly RunCatalog _catalog;
    private NormalizedDocument? _normalized;

    public ReviewModel(RunCatalog catalog) => _catalog = catalog;

    public LoadedRun Run { get; private set; } = null!;

    public string? Reviewer { get; private set; }

    public string? Error { get; private set; }

    public string? Unavailable { get; private set; }

    public bool ReadOnly { get; private set; }

    public string? Decider => ReadOnly ? null : Reviewer;

    public Task<IActionResult> OnGetAsync(string key, CancellationToken cancellationToken) =>
        ShowAsync(key, error: null, cancellationToken);

    public IActionResult OnPostReviewer(string key, string? name)
    {
        if (!Workspace.IsRunKey(key))
        {
            return NotFound();
        }

        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            Response.Cookies.Delete(SpecTraceWebApp.ReviewerCookie);
        }
        else
        {
            Response.Cookies.Append(
                SpecTraceWebApp.ReviewerCookie,
                Uri.EscapeDataString(trimmed),
                new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true, Path = "/" });
        }

        return Redirect($"/runs/{key}/review");
    }

    public async Task<IActionResult> OnPostCaseAsync(
        string key,
        string? caseId,
        string? decision,
        string? author,
        string? title,
        string? type,
        string? precondition,
        string? input,
        string? expectedResult,
        CancellationToken cancellationToken)
    {
        if (!Workspace.IsRunKey(key))
        {
            return NotFound();
        }

        if (_catalog.IsReadOnly(key))
        {
            return await RefuseReadOnlyAsync(key, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(author))
        {
            return await ShowAsync(key, "Enter your name at the top of the page before deciding. Nothing was logged.", cancellationToken);
        }

        try
        {
            var chosen = Spelled.Parse<CaseDecision>(decision ?? string.Empty, ReviewLog.Spell);
            var edit = chosen == CaseDecision.Edit
                ? new CaseEdit(
                    Normalized(title),
                    Spelled.Parse<CaseType>(type ?? string.Empty, RunArtifacts.Spell),
                    Normalized(precondition),
                    Normalized(input),
                    Normalized(expectedResult))
                : null;

            await _catalog.RecordCaseDecisionAsync(key, caseId ?? string.Empty, chosen, edit, author.Trim(), cancellationToken);
        }
        catch (Exception exception) when (exception is ReviewRefusedException or FormatException or ArgumentException)
        {
            return await ShowAsync(key, Message(exception), cancellationToken);
        }
        catch (RunReadOnlyException)
        {
            return await RefuseReadOnlyAsync(key, cancellationToken);
        }
        catch (RunNotAvailableException)
        {
            return await ShowAsync(key, error: null, cancellationToken);
        }

        return Redirect($"/runs/{key}/review#{Uri.EscapeDataString(caseId ?? string.Empty)}");
    }

    public async Task<IActionResult> OnPostItemAsync(
        string key,
        string? itemId,
        string? decision,
        string? author,
        CancellationToken cancellationToken)
    {
        if (!Workspace.IsRunKey(key))
        {
            return NotFound();
        }

        if (_catalog.IsReadOnly(key))
        {
            return await RefuseReadOnlyAsync(key, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(author))
        {
            return await ShowAsync(key, "Enter your name at the top of the page before deciding. Nothing was logged.", cancellationToken);
        }

        try
        {
            var chosen = Spelled.Parse<QueueDecision>(decision ?? string.Empty, ReviewLog.Spell);

            await _catalog.RecordQueueDecisionAsync(key, itemId ?? string.Empty, chosen, author.Trim(), cancellationToken);
        }
        catch (Exception exception) when (exception is ReviewRefusedException or FormatException)
        {
            return await ShowAsync(key, Message(exception), cancellationToken);
        }
        catch (RunReadOnlyException)
        {
            return await RefuseReadOnlyAsync(key, cancellationToken);
        }
        catch (RunNotAvailableException)
        {
            return await ShowAsync(key, error: null, cancellationToken);
        }

        return Redirect($"/runs/{key}/review#{Uri.EscapeDataString(itemId ?? string.Empty)}");
    }

    public SourceContext ContextFor(Requirement requirement) => SourceContext.Around(Run.Document, requirement.Span);

    public IReadOnlyList<SourceContext> ContextsFor(string quote)
    {
        _normalized ??= NormalizedDocument.Create(Run.Document);

        return _normalized.Occurrences(quote)
            .Select(span => SourceContext.Around(Run.Document, span))
            .ToList();
    }

    public IReadOnlyList<ReviewedCase> CasesFor(string requirementId) =>
        Run.Reviewed.Cases
            .Where(reviewed => reviewed.Original.RequirementIds.Contains(requirementId, StringComparer.Ordinal))
            .ToList();

    public QueuedDecision QueuedFor(ReviewedItem item) =>
        Run.Contents.DecisionQueue.Single(decision => decision.Item.Id == item.Entry.ItemId);

    private async Task<IActionResult> ShowAsync(string key, string? error, CancellationToken cancellationToken)
    {
        if (!Workspace.IsRunKey(key) || _catalog.Find(key) is null)
        {
            return NotFound();
        }

        try
        {
            Run = await _catalog.LoadAsync(key, cancellationToken);
        }
        catch (RunNotAvailableException exception)
        {
            Unavailable = exception.Message;
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        Reviewer = Request.Cookies.TryGetValue(SpecTraceWebApp.ReviewerCookie, out var cookie)
            ? Uri.UnescapeDataString(cookie)
            : null;
        ReadOnly = _catalog.IsReadOnly(key);
        Error = error;

        if (error is not null)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }

        return Page();
    }

    private async Task<IActionResult> RefuseReadOnlyAsync(string key, CancellationToken cancellationToken)
    {
        var result = await ShowAsync(key, RunCatalog.ReadOnlyReason, cancellationToken);

        if (result is PageResult && Unavailable is null)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
        }

        return result;
    }

    private static string Normalized(string? text) => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Message(Exception exception) =>
        exception is ArgumentException { ParamName: { } name } argument
            ? argument.Message.Replace($" (Parameter '{name}')", string.Empty, StringComparison.Ordinal) + " Nothing was logged."
            : exception is ReviewRefusedException ? exception.Message : $"{exception.Message} Nothing was logged.";
}
