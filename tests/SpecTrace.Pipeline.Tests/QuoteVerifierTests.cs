using System.Text.Json;
using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

public sealed class QuoteVerifierTests
{
    private const string RealQuote =
        "The operation object MUST contain a \"value\" member whose content specifies the value to be added.";

    private const string SecondRealQuote =
        "The operation object MUST contain a \"value\" member whose content specifies the replacement value.";

    private const string RepeatedQuote = "A JSON Patch document:";

    [Fact]
    public async Task AFabricatedQuoteIsDroppedFromTheRegisterAndRecordedVerbatimAsRejected()
    {
        const string OneWordOff =
            "  The operation object MUST include a \"value\" member whose content specifies the value to be added.  ";
        const string Invented = "Implementations MUST  reject patches larger than 64 kilobytes.";

        var model = new ScriptedLlmClient(ModelAnswer.With(
            ("MUST", RealQuote, "testable"),
            ("MUST", OneWordOff, "testable"),
            ("MUST", Invented, "testable")));

        var extraction = await new RequirementExtractor(model, "gemini-3.5-flash")
            .ExtractAsync(Corpus.DocumentId, Corpus.Raw, CancellationToken.None);
        var outcome = Corpus.Verifier().Verify(extraction.Candidates);

        using var run = new ScratchDirectory();
        await RunArtifacts.WriteAsync(outcome, run.Path, CancellationToken.None);

        var register = Assert.Single(outcome.Register);
        Assert.Equal(RealQuote, register.Text);

        var requirementsJson = await File.ReadAllTextAsync(
            Path.Combine(run.Path, RunArtifacts.RequirementsFile), CancellationToken.None);
        Assert.DoesNotContain("MUST include", requirementsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("64 kilobytes", requirementsJson, StringComparison.Ordinal);

        using var rejectedJson = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(run.Path, RunArtifacts.RejectedQuotesFile), CancellationToken.None));
        var rejected = rejectedJson.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, rejected.Count);
        Assert.Equal(OneWordOff, rejected[0].GetProperty("quote").GetString());
        Assert.Equal(Invented, rejected[1].GetProperty("quote").GetString());
        Assert.All(rejected, entry =>
        {
            Assert.Equal("failed", entry.GetProperty("verification").GetString());
            Assert.Equal(RejectedQuote.NotFound, entry.GetProperty("reason").GetString());
        });
    }

    [Fact]
    public void AQuoteFoundMoreThanOnceBecomesAQuestionForAPersonAndNotARequirement()
    {
        var outcome = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Must, RepeatedQuote, Testability.Testable, null),
        ]);

        Assert.Empty(outcome.Register);
        Assert.Empty(outcome.Rejected);

        var decision = Assert.Single(outcome.Decisions);
        Assert.Equal(QueuedDecision.QuoteFoundMoreThanOnce, decision.Reason);
        Assert.Equal(Verification.Ambiguous, decision.Verification);
        Assert.Equal(RepeatedQuote, decision.Item.Quote);
        Assert.Equal(DecisionQueueItem.NoSingleSection, decision.Item.Section);
        Assert.Null(decision.Item.Resolution);
        Assert.StartsWith("DQ-rfc6902-", decision.Item.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameQuoteClaimedWithTwoModalitiesGoesToAPersonWithBothReadingsInsteadOfKeepingTheFirst()
    {
        CandidateRequirement should = new(Modality.Should, RealQuote, Testability.Testable, null);
        CandidateRequirement mustNot = new(Modality.MustNot, RealQuote, Testability.Testable, null);

        var outcome = Corpus.Verifier().Verify([should, mustNot]);

        Assert.Empty(outcome.Register);
        Assert.Empty(outcome.Rejected);

        var decision = Assert.Single(outcome.Decisions);
        Assert.Equal(QueuedDecision.ConflictingReadings, decision.Reason);
        Assert.Equal(Verification.Exact, decision.Verification);
        Assert.Equal("4.1", decision.Item.Section);
        Assert.Equal([should, mustNot], decision.Claims);
        Assert.Contains("SHOULD", decision.Item.Question, StringComparison.Ordinal);
        Assert.Contains("MUST_NOT", decision.Item.Question, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameQuoteClaimedWithTwoTestabilitiesIsAlsoLeftToAPerson()
    {
        var outcome = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Must, RealQuote, Testability.Testable, null),
            new CandidateRequirement(Modality.Must, RealQuote, Testability.NeedsHumanDecision, "Depends on X."),
        ]);

        Assert.Empty(outcome.Register);
        Assert.Equal(QueuedDecision.ConflictingReadings, Assert.Single(outcome.Decisions).Reason);
    }

    [Fact]
    public async Task AConflictingQuoteIsWrittenToTheDecisionsFileWithEveryReading()
    {
        var outcome = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Should, RealQuote, Testability.Testable, null),
            new CandidateRequirement(Modality.MustNot, RealQuote, Testability.Testable, null),
        ]);

        using var run = new ScratchDirectory();
        await RunArtifacts.WriteAsync(outcome, run.Path, CancellationToken.None);

        using var decisions = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(run.Path, RunArtifacts.DecisionsFile), CancellationToken.None));
        var entry = Assert.Single(decisions.RootElement.EnumerateArray().ToList());

        Assert.Equal(QueuedDecision.ConflictingReadings, entry.GetProperty("reason").GetString());
        Assert.Equal("exact", entry.GetProperty("verification").GetString());
        Assert.Equal(
            ["SHOULD", "MUST_NOT"],
            entry.GetProperty("claims").EnumerateArray().Select(claim => claim.GetProperty("modality").GetString()));
    }

    [Fact]
    public void EveryClaimedReadingEndsUpInTheRegisterTheRejectedReportOrTheDecisionQueue()
    {
        CandidateRequirement[] candidates =
        [
            new(Modality.Must, RealQuote, Testability.Testable, null),
            new(Modality.Must, RealQuote, Testability.Testable, null),
            new(Modality.Should, SecondRealQuote, Testability.Testable, null),
            new(Modality.MustNot, SecondRealQuote, Testability.Testable, null),
            new(Modality.Must, RepeatedQuote, Testability.Testable, null),
            new(Modality.May, RepeatedQuote, Testability.Testable, null),
            new(Modality.Must, "Not a sentence in RFC 6902.", Testability.Testable, null),
        ];

        Readings.AssertNoneDisappeared(candidates, Corpus.Verifier().Verify(candidates));
    }

    [Fact]
    public void TheVerificationRateDividesQuotesLocatedExactlyOnceByEveryQuoteReturned()
    {
        var outcome = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Must, RealQuote, Testability.Testable, null),
            new CandidateRequirement(Modality.Must, RealQuote, Testability.Testable, null),
            new CandidateRequirement(Modality.Must, RepeatedQuote, Testability.Testable, null),
            new CandidateRequirement(Modality.Must, "Not a sentence in RFC 6902.", Testability.Testable, null),
        ]);

        Assert.Equal(4, outcome.ClaimCount);
        Assert.Equal(2, outcome.ExactClaimCount);
        Assert.Equal(1, outcome.AmbiguousClaimCount);
        Assert.Single(outcome.Rejected);
        Assert.Single(outcome.Register);
        Assert.Equal(0.5, outcome.VerificationRate);
    }

    [Fact]
    public async Task AnAmbiguousQuoteIsWrittenToTheDecisionsFileMarkedAmbiguous()
    {
        var outcome = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Must, RepeatedQuote, Testability.Testable, null),
        ]);

        using var run = new ScratchDirectory();
        await RunArtifacts.WriteAsync(outcome, run.Path, CancellationToken.None);

        using var decisions = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(run.Path, RunArtifacts.DecisionsFile), CancellationToken.None));
        var entry = Assert.Single(decisions.RootElement.EnumerateArray().ToList());

        Assert.Equal("ambiguous", entry.GetProperty("verification").GetString());
        Assert.Equal(RepeatedQuote, entry.GetProperty("quote").GetString());
    }

    [Fact]
    public void NoFailedOrAmbiguousClaimReachesTheRegister()
    {
        var outcome = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Must, RealQuote, Testability.Testable, null),
            new CandidateRequirement(Modality.Must, "Not a sentence in RFC 6902.", Testability.Testable, null),
            new CandidateRequirement(Modality.Must, RepeatedQuote, Testability.Testable, null),
            new CandidateRequirement(Modality.Must, SecondRealQuote, Testability.Testable, null),
        ]);

        Assert.Equal(2, outcome.Register.Count);
        Assert.Single(outcome.Rejected);
        Assert.Single(outcome.Decisions);
        Assert.All(outcome.Register, requirement => Assert.Equal(Verification.Exact, requirement.Verification));
        Assert.Equal(0.5, outcome.VerificationRate);
    }

    [Fact]
    public void EveryVerifiedRequirementsRawSpanNormalisesExactlyToItsQuote()
    {
        var outcome = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Must, RealQuote, Testability.Testable, null),
            new CandidateRequirement(Modality.Must, SecondRealQuote, Testability.Testable, null),
        ]);

        Assert.Equal(2, outcome.Register.Count);

        foreach (var requirement in outcome.Register)
        {
            var raw = Corpus.Raw[requirement.Span.Start..requirement.Span.End];

            Assert.Equal(TextNormalizer.Normalize(requirement.Text), TextNormalizer.Normalize(raw));
        }
    }

    [Fact]
    public void AVerifiedRequirementNamesTheSectionItsSpanFallsIn()
    {
        var outcome = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Must, RealQuote, Testability.Testable, null),
            new CandidateRequirement(Modality.Must, SecondRealQuote, Testability.Testable, null),
        ]);

        Assert.Equal("4.1", outcome.Register[0].Section);
        Assert.Equal("4.3", outcome.Register[1].Section);
    }

    [Fact]
    public void ARequirementIdHasTheShapeOfPlanSection43()
    {
        var requirement = Assert.Single(Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Must, RealQuote, Testability.Testable, null),
        ]).Register);

        Assert.Matches("^REQ-rfc6902-[0-9a-f]{6}$", requirement.Id);
    }

    [Fact]
    public void TheSameQuotesGetTheSameIdsOnEveryRunWhateverOrderTheyArriveIn()
    {
        CandidateRequirement[] forwards =
        [
            new(Modality.Must, RealQuote, Testability.Testable, null),
            new(Modality.Must, SecondRealQuote, Testability.Testable, null),
        ];

        var first = Corpus.Verifier().Verify(forwards).Register.Select(r => r.Id).ToHashSet();
        var again = Corpus.Verifier().Verify(forwards).Register.Select(r => r.Id).ToHashSet();
        var reversed = Corpus.Verifier().Verify(forwards.Reverse().ToList()).Register.Select(r => r.Id).ToHashSet();

        Assert.Equal(first, again);
        Assert.Equal(first, reversed);
    }

    [Fact]
    public void AQuoteTheModelWrappedDifferentlyStillGetsTheSameId()
    {
        var tidy = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Must, RealQuote, Testability.Testable, null),
        ]).Register.Single();

        var wrapped = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(
                Modality.Must,
                RealQuote.Replace(" member ", "\n   member  ", StringComparison.Ordinal),
                Testability.Testable,
                null),
        ]).Register.Single();

        Assert.Equal(tidy.Id, wrapped.Id);
        Assert.Equal(tidy.Span, wrapped.Span);
    }

    [Fact]
    public void AStatementClaimedTwiceBecomesOneRequirement()
    {
        var outcome = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Must, RealQuote, Testability.Testable, null),
            new CandidateRequirement(Modality.Must, RealQuote, Testability.Testable, null),
        ]);

        Assert.Single(outcome.Register);
    }

    [Fact]
    public void TwoDifferentQuotesThatHashToTheSameIdStopTheRunRatherThanMerging()
    {
        const string First = ". . . . . . . . . . . . . . . . . 7 4.6. test";
        const string Second = "umber(s): N/A File extension(s): .json-p";

        var verifier = Corpus.Verifier();

        Assert.Equal("REQ-rfc6902-6715bf", verifier.IdFor(First));
        Assert.Equal("REQ-rfc6902-6715bf", verifier.IdFor(Second));
        Assert.NotEqual(First, Second);

        var exception = Assert.Throws<InvalidOperationException>(() => verifier.Verify(
        [
            new CandidateRequirement(Modality.Must, First, Testability.Testable, null),
            new CandidateRequirement(Modality.Must, Second, Testability.Testable, null),
        ]));

        Assert.Contains("REQ-rfc6902-6715bf", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyQuoteIsRejectedAsEmptyRatherThanMatchedAtTheStartOfTheDocument()
    {
        var outcome = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Must, "   ", Testability.Testable, null),
        ]);

        Assert.Empty(outcome.Register);
        var rejected = Assert.Single(outcome.Rejected);
        Assert.Equal("   ", rejected.Quote);
        Assert.Equal(RejectedQuote.EmptyQuote, rejected.Reason);
    }

    [Fact]
    public void TheModelsModalityAndTestabilityAreCarriedOntoTheRequirementUnchanged()
    {
        var requirement = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Should, RealQuote, Testability.NeedsHumanDecision, "Depends on X."),
        ]).Register.Single();

        Assert.Equal(Modality.Should, requirement.Modality);
        Assert.Equal(Testability.NeedsHumanDecision, requirement.Testability);
        Assert.Equal("Depends on X.", requirement.TestabilityNote);
    }
}
