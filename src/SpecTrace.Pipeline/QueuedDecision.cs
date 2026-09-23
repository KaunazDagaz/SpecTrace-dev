using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public sealed record QueuedDecision(
    DecisionQueueItem Item,
    string Reason,
    Verification Verification,
    IReadOnlyList<CandidateRequirement> Claims)
{
    public const string QuoteFoundMoreThanOnce = "quote_found_more_than_once";

    public const string ConflictingReadings = "same_quote_claimed_with_different_readings";
}
