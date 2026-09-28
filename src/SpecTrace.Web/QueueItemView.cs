using SpecTrace.Core;
using SpecTrace.Pipeline;

namespace SpecTrace.Web;

public sealed record QueueItemView(
    string Key,
    string? Reviewer,
    ReviewedItem Item,
    QueuedDecision Queued,
    IReadOnlyList<SourceContext> Occurrences)
{
    public string ModelNote =>
        Queued.BlockedReason
        ?? string.Join(
            " ",
            Queued.Claims
                .Select(claim => claim.TestabilityNote)
                .Where(note => !string.IsNullOrWhiteSpace(note))
                .Distinct(StringComparer.Ordinal));
}
