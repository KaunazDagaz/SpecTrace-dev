using SpecTrace.Core;
using SpecTrace.Llm;

namespace SpecTrace.Pipeline.Tests;

public sealed class TestCaseGeneratorTests
{
    private const string AddValueQuote =
        "The operation object MUST contain a \"value\" member whose content specifies the value to be added.";

    private const string WellFormedCase =
        """{ "title": "t", "type": "positive", "precondition": "p", "input": "i", "expected_result": "e" }""";

    [Fact]
    public void AWellFormedAnswerIsReadIntoCasesInTheOrderGiven()
    {
        var answer = TestCaseGenerator.Parse(GenerationAnswerJson.Cases(
            ("positive", "Adding a value succeeds"),
            ("negative", "An add without a value is rejected"),
            ("boundary", "An empty value is added")));

        Assert.Equal(
            [CaseType.Positive, CaseType.Negative, CaseType.Boundary],
            answer.Cases.Select(generated => generated.Type));
        Assert.Equal("Adding a value succeeds", answer.Cases[0].Title);
        Assert.Null(answer.BlockedReason);
    }

    [Fact]
    public void ABlockedAnswerHasNoCasesAndCarriesTheModelsReason()
    {
        var answer = TestCaseGenerator.Parse(GenerationAnswerJson.Blocked("The quote names no observable behaviour."));

        Assert.Empty(answer.Cases);
        Assert.Equal("The quote names no observable behaviour.", answer.BlockedReason);
    }

    [Fact]
    public void AnEmptyPreconditionIsAccepted()
    {
        var answer = TestCaseGenerator.Parse(
            """{ "cases": [ { "title": "t", "type": "negative", "precondition": "", "input": "i", "expected_result": "e" } ] }""");

        Assert.Equal(string.Empty, Assert.Single(answer.Cases).Precondition);
    }

    [Theory]
    [InlineData("Here are the test cases:")]
    [InlineData("```json\n{ \"cases\": [], \"blocked_reason\": \"x\" }\n```")]
    [InlineData("[]")]
    [InlineData("""{ "blocked_reason": null }""")]
    [InlineData("""{ "cases": {} }""")]
    [InlineData("""{ "cases": [ "a case" ] }""")]
    [InlineData("""{ "cases": [], "blocked_reason": null }""")]
    [InlineData("""{ "cases": [], "blocked_reason": "   " }""")]
    [InlineData("""{ "cases": [], "blocked_reason": 42 }""")]
    [InlineData("""{ "cases": [ { "title": "t", "type": "positive", "precondition": "p", "input": "i", "expected_result": "e" } ], "blocked_reason": "also blocked" }""")]
    [InlineData("""{ "cases": [ { "title": "t", "type": "exploratory", "precondition": "p", "input": "i", "expected_result": "e" } ] }""")]
    [InlineData("""{ "cases": [ { "title": " ", "type": "positive", "precondition": "p", "input": "i", "expected_result": "e" } ] }""")]
    [InlineData("""{ "cases": [ { "title": "t", "type": "positive", "precondition": "p", "input": "", "expected_result": "e" } ] }""")]
    [InlineData("""{ "cases": [ { "title": "t", "type": "positive", "precondition": "p", "input": "i", "expected_result": " " } ] }""")]
    [InlineData("""{ "cases": [ { "title": "t", "type": "positive", "precondition": null, "input": "i", "expected_result": "e" } ] }""")]
    [InlineData("""{ "cases": [ { "title": 7, "type": "positive", "precondition": "p", "input": "i", "expected_result": "e" } ] }""")]
    public void AResponseThatDoesNotMatchTheSchemaIsAFailedCall(string response)
    {
        Assert.Throws<LlmResponseException>(() => TestCaseGenerator.Parse(response));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("type")]
    [InlineData("precondition")]
    [InlineData("input")]
    [InlineData("expected_result")]
    public void ACaseMissingAnyOfItsFiveFieldsIsAFailedCall(string missing)
    {
        var fields = new Dictionary<string, string>
        {
            ["title"] = "t",
            ["type"] = "positive",
            ["precondition"] = "p",
            ["input"] = "i",
            ["expected_result"] = "e",
        };
        fields.Remove(missing);

        var response = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["cases"] = new[] { fields },
            ["blocked_reason"] = null,
        });

        Assert.Throws<LlmResponseException>(() => TestCaseGenerator.Parse(response));
    }

    [Fact]
    public void MoreThanThreeCasesIsAFailedCallNotATruncation()
    {
        var response = $$"""{ "cases": [ {{WellFormedCase}}, {{WellFormedCase}}, {{WellFormedCase}}, {{WellFormedCase}} ] }""";

        Assert.Contains("4 cases", Assert.Throws<LlmResponseException>(() => TestCaseGenerator.Parse(response)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AResponseThatNamesARequirementIdIsAFailedCallBecauseOnlyTheCodeAttachesIds()
    {
        var onTheCase = """{ "cases": [ { "title": "t", "type": "positive", "precondition": "p", "input": "i", "expected_result": "e", "requirement_ids": ["REQ-rfc6902-000000"] } ] }""";
        var onTheAnswer = $$"""{ "cases": [ {{WellFormedCase}} ], "requirement_id": "REQ-rfc6902-000000" }""";

        Assert.Contains("requirement_ids", Assert.Throws<LlmResponseException>(() => TestCaseGenerator.Parse(onTheCase)).Message, StringComparison.Ordinal);
        Assert.Contains("requirement_id", Assert.Throws<LlmResponseException>(() => TestCaseGenerator.Parse(onTheAnswer)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCodeAttachesTheIdOfTheRequirementItAskedAboutAndEveryCaseIsAProposal()
    {
        var requirement = AddValueRequirement();
        var model = new ScriptedLlmClient(GenerationAnswerJson.Cases(("positive", "a"), ("negative", "b")));

        var generation = await new TestCaseGenerator(model, LlmClientFactory.DefaultModel)
            .GenerateAsync(requirement, CancellationToken.None);

        Assert.Equal(requirement.Id, generation.RequirementId);
        Assert.Equal(2, generation.Cases.Count);
        Assert.All(generation.Cases, testCase =>
        {
            Assert.Equal([requirement.Id], testCase.RequirementIds);
            Assert.Equal(ReviewStatus.Proposed, testCase.Status);
        });
        Assert.Null(generation.BlockedReason);
    }

    [Fact]
    public async Task CaseIdsComeFromTheRequirementIdAndAnOrdinalAndAreTheSameOnEveryRun()
    {
        var requirement = AddValueRequirement();
        var answer = GenerationAnswerJson.Cases(("positive", "a"), ("negative", "b"), ("boundary", "c"));
        var hash = requirement.Id[(requirement.Id.LastIndexOf('-') + 1)..];

        var first = await new TestCaseGenerator(new ScriptedLlmClient(answer), LlmClientFactory.DefaultModel)
            .GenerateAsync(requirement, CancellationToken.None);
        var again = await new TestCaseGenerator(new ScriptedLlmClient(answer), LlmClientFactory.DefaultModel)
            .GenerateAsync(requirement, CancellationToken.None);

        Assert.Equal([$"TC-{hash}-01", $"TC-{hash}-02", $"TC-{hash}-03"], first.Cases.Select(testCase => testCase.Id));
        Assert.Equal(first.Cases.Select(testCase => testCase.Id), again.Cases.Select(testCase => testCase.Id));
        Assert.All(first.Cases, testCase => Assert.Matches("^TC-[0-9a-f]{6}-0[1-3]$", testCase.Id));
    }

    [Fact]
    public async Task AMalformedResponseFailsTheCallNamingTheRequirementAndYieldsNoCase()
    {
        var requirement = AddValueRequirement();
        var model = new ScriptedLlmClient($$"""{ "cases": [ {{WellFormedCase}}, { "title": "t" } ] }""");

        var exception = await Assert.ThrowsAsync<LlmResponseException>(() =>
            new TestCaseGenerator(model, LlmClientFactory.DefaultModel).GenerateAsync(requirement, CancellationToken.None));

        Assert.Contains(requirement.Id, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Testability.NeedsHumanDecision)]
    [InlineData(Testability.NotTestable)]
    public async Task NoCaseIsGeneratedForARequirementTheModelFlagged(Testability flagged)
    {
        var requirement = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Must, AddValueQuote, flagged, "Depends on X."),
        ]).Register.Single();
        var model = new ScriptedLlmClient(GenerationAnswerJson.Cases(("positive", "a")));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new TestCaseGenerator(model, LlmClientFactory.DefaultModel).GenerateAsync(requirement, CancellationToken.None));

        Assert.Equal(0, model.Calls);
    }

    private static Requirement AddValueRequirement() =>
        Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Must, AddValueQuote, Testability.Testable, null),
        ]).Register.Single();
}
