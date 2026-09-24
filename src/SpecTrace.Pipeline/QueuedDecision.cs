using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public sealed record QueuedDecision(
    DecisionQueueItem Item,
    string Reason,
    Verification Verification,
    IReadOnlyList<CandidateRequirement> Claims,
    string? RequirementId = null,
    string? BlockedReason = null)
{
    public const string QuoteFoundMoreThanOnce = "quote_found_more_than_once";

    public const string ConflictingReadings = "same_quote_claimed_with_different_readings";

    public const string ModelFlaggedNeedsHumanDecision = "model_flagged_needs_human_decision";

    public const string ModelFlaggedNotTestable = "model_flagged_not_testable";

    public const string GenerationBlocked = "generation_blocked";

    private const string RequirementIdPrefix = "REQ-";

    private const string NeedsHumanDecisionQuestion =
        "The model judged that this requirement cannot be checked from its own text alone, so no test "
        + "case was generated for it. It stays a gap until a person decides: can it be tested from its "
        + "text, is it not testable, or should it be deferred?";

    private const string NotTestableQuestion =
        "The model judged this requirement not testable, so no test case was generated for it. It stays "
        + "a gap until a person decides: is it not testable, should it be deferred, or should test cases "
        + "be written for it?";

    private const string GenerationBlockedQuestion =
        "The model wrote no test case from this quote and gave the reason recorded with this item. The "
        + "requirement stays a gap until a person decides: can a case be written from the quote, is it "
        + "not testable, or should it be deferred?";

    public static QueuedDecision ModelFlagged(Requirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        var (reason, question) = requirement.Testability switch
        {
            Testability.NeedsHumanDecision => (ModelFlaggedNeedsHumanDecision, NeedsHumanDecisionQuestion),
            Testability.NotTestable => (ModelFlaggedNotTestable, NotTestableQuestion),
            _ => throw new ArgumentException(
                $"Requirement '{requirement.Id}' is testable; only a requirement the model flagged is queued.",
                nameof(requirement)),
        };

        return ForRequirement(requirement, reason, question, blockedReason: null);
    }

    public static QueuedDecision Blocked(Requirement requirement, string blockedReason)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentException.ThrowIfNullOrWhiteSpace(blockedReason);

        return ForRequirement(requirement, GenerationBlocked, GenerationBlockedQuestion, blockedReason);
    }

    private static QueuedDecision ForRequirement(
        Requirement requirement,
        string reason,
        string question,
        string? blockedReason)
    {
        if (!requirement.Id.StartsWith(RequirementIdPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Requirement ID '{requirement.Id}' does not start with '{RequirementIdPrefix}'.",
                nameof(requirement));
        }

        return new QueuedDecision(
            new DecisionQueueItem(
                $"DQ-{requirement.Id[RequirementIdPrefix.Length..]}",
                requirement.Text,
                requirement.Section,
                question,
                resolution: null),
            reason,
            requirement.Verification,
            [
                new CandidateRequirement(
                    requirement.Modality,
                    requirement.Text,
                    requirement.Testability,
                    requirement.TestabilityNote),
            ],
            requirement.Id,
            blockedReason);
    }
}
