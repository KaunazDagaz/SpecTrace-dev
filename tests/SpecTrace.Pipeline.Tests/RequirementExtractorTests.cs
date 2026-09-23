using SpecTrace.Core;
using SpecTrace.Llm;

namespace SpecTrace.Pipeline.Tests;

public sealed class RequirementExtractorTests
{
    [Fact]
    public void AllFiveModalitiesAndAllThreeTestabilityValuesAreRead()
    {
        var candidates = RequirementExtractor.Parse(ModelAnswer.With(
            ("MUST", "a", "testable"),
            ("MUST_NOT", "b", "needs_human_decision"),
            ("SHOULD", "c", "not_testable"),
            ("SHOULD_NOT", "d", "testable"),
            ("MAY", "e", "testable")));

        Assert.Equal(
            [Modality.Must, Modality.MustNot, Modality.Should, Modality.ShouldNot, Modality.May],
            candidates.Select(candidate => candidate.Modality));
        Assert.Equal(
            [Testability.Testable, Testability.NeedsHumanDecision, Testability.NotTestable],
            candidates.Take(3).Select(candidate => candidate.Testability));
    }

    [Fact]
    public void TheQuoteIsKeptExactlyAsTheModelWroteIt()
    {
        const string Odd = "  spaced \n and wrapped  ";

        var candidate = Assert.Single(RequirementExtractor.Parse(ModelAnswer.With(("MUST", Odd, "testable"))));

        Assert.Equal(Odd, candidate.Quote);
    }

    [Fact]
    public void AnUnknownModalityStopsTheRunRatherThanBeingDefaultedToMust()
    {
        var exception = Assert.Throws<LlmResponseException>(
            () => RequirementExtractor.Parse(ModelAnswer.With(("REQUIRED", "a", "testable"))));

        Assert.Contains("REQUIRED", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownTestabilityStopsTheRun()
    {
        Assert.Throws<LlmResponseException>(
            () => RequirementExtractor.Parse(ModelAnswer.With(("MUST", "a", "maybe"))));
    }

    [Fact]
    public void AnAnswerThatIsNotJsonStopsTheRun()
    {
        Assert.Throws<LlmResponseException>(() => RequirementExtractor.Parse("Here are the requirements:"));
    }

    [Fact]
    public void AnAnswerWrappedInMarkdownFencesStopsTheRunRatherThanBeingUnwrapped()
    {
        Assert.Throws<LlmResponseException>(() => RequirementExtractor.Parse("```json\n[]\n```"));
    }

    [Fact]
    public void AnAnswerThatIsAnObjectRatherThanAnArrayStopsTheRun()
    {
        Assert.Throws<LlmResponseException>(() => RequirementExtractor.Parse("""{ "requirements": [] }"""));
    }

    [Fact]
    public void AnEntryWithNoQuoteStopsTheRun()
    {
        Assert.Throws<LlmResponseException>(
            () => RequirementExtractor.Parse("""[ { "modality": "MUST", "testability": "testable" } ]"""));
    }

    [Fact]
    public void AnEmptyQuoteIsCarriedThroughSoVerificationCanRecordItsRejection()
    {
        var candidate = Assert.Single(RequirementExtractor.Parse(ModelAnswer.With(("MUST", "", "testable"))));

        Assert.Equal(string.Empty, candidate.Quote);
    }

    [Fact]
    public void AnEmptyAnswerIsAnEmptyListNotAnError()
    {
        Assert.Empty(RequirementExtractor.Parse("[]"));
    }

    [Fact]
    public async Task TheWholeDocumentGoesOutInOneRequest()
    {
        var model = new ScriptedLlmClient("[]");

        await new RequirementExtractor(model, "gemini-3.5-flash")
            .ExtractAsync(Corpus.DocumentId, Corpus.Raw, CancellationToken.None);

        Assert.Equal(1, model.Calls);
        Assert.Contains(Corpus.Raw, model.LastRequest!.UserPrompt, StringComparison.Ordinal);
    }
}
