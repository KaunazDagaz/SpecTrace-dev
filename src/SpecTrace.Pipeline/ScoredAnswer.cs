using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public sealed record ScoredClaim(ClaimedQuote Claim, ClaimOutcome Outcome);

public sealed class ScoredAnswer
{
    private ScoredAnswer(IReadOnlyList<ScoredClaim> claims, string? failure)
    {
        Claims = claims;
        Failure = failure;
    }

    public IReadOnlyList<ScoredClaim> Claims { get; }

    public string? Failure { get; }

    public ClaimTally? Tally => Failure is null ? ClaimTally.Of(Claims.Select(claim => claim.Outcome)) : null;

    public static ScoredAnswer Of(string answer, NormalizedDocument document)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(document);

        IReadOnlyList<ClaimedQuote> claims;

        try
        {
            claims = ClaimParser.Parse(answer);
        }
        catch (UnparseableAnswerException exception)
        {
            return new ScoredAnswer([], exception.Message);
        }

        return new ScoredAnswer(
            claims
                .Select(claim => new ScoredClaim(
                    claim,
                    ClaimTally.OutcomeOf(claim.Quote is null ? null : document.Resolve(claim.Quote))))
                .ToList(),
            failure: null);
    }
}
