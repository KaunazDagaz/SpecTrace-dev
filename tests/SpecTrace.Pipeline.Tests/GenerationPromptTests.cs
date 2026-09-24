using System.Text.Json;
using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

public sealed class GenerationPromptTests
{
    private const string AddValueQuote =
        "The operation object MUST contain a \"value\" member whose content specifies the value to be added.";

    [Fact]
    public void TheResponseSchemaDeclaresNoFieldThatCouldCarryAPosition()
    {
        using var schema = JsonDocument.Parse(GenerationSchema.Json);
        var names = SchemaInspection.PropertyNamesIn(schema.RootElement).ToList();

        Assert.NotEmpty(names);

        foreach (var name in names)
        {
            foreach (var forbidden in SchemaInspection.PositionLikeNames)
            {
                Assert.False(
                    name.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"Schema property '{name}' looks like it could carry a position ('{forbidden}').");
            }
        }
    }

    [Fact]
    public void TheResponseSchemaGivesTheModelNoWayToNameARequirement()
    {
        using var schema = JsonDocument.Parse(GenerationSchema.Json);

        foreach (var name in SchemaInspection.PropertyNamesIn(schema.RootElement))
        {
            Assert.DoesNotContain("requirement", name, StringComparison.OrdinalIgnoreCase);
            Assert.False(
                name == "id" || name.EndsWith("_id", StringComparison.Ordinal) || name.EndsWith("_ids", StringComparison.Ordinal),
                $"Schema property '{name}' could carry an identifier.");
        }
    }

    [Fact]
    public void TheResponseSchemaDeclaresExactlyTheFieldsOfPlanSection72()
    {
        using var schema = JsonDocument.Parse(GenerationSchema.Json);
        var root = schema.RootElement.GetProperty("properties");

        Assert.Equal(
            ["blocked_reason", "cases"],
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["expected_result", "input", "precondition", "title", "type"],
            root.GetProperty("cases").GetProperty("items").GetProperty("properties")
                .EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ThePromptIsTheP2PromptOfPlanSection72()
    {
        var text = PromptFile.Generation.Text;

        Assert.StartsWith("You write black-box test cases for a single requirement.\n", text, StringComparison.Ordinal);
        Assert.Contains("You are given one requirement quote and its section number.", text, StringComparison.Ordinal);
        Assert.Contains("1. Produce between 1 and 3 cases.", text, StringComparison.Ordinal);
        Assert.Contains(
            "case, return an empty array and give the reason in \"blocked_reason\".",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public void TheOutputSchemaWrittenInThePromptDeclaresTheSameFieldsAndNoPosition()
    {
        var text = PromptFile.Generation.Text;
        var declared = text[text.IndexOf("Output schema:", StringComparison.Ordinal)..];

        foreach (var field in new[] { "\"cases\"", "\"title\"", "\"type\"", "\"precondition\"", "\"input\"", "\"expected_result\"", "\"blocked_reason\"" })
        {
            Assert.Contains(field, declared, StringComparison.Ordinal);
        }

        foreach (var forbidden in SchemaInspection.PositionLikeNames)
        {
            Assert.DoesNotContain($"\"{forbidden}", declared, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EveryGenerationRequestIsAtTemperatureZeroWithTheSchemaAndThePromptHash()
    {
        var request = Generator().RequestFor(AddValueRequirement());

        Assert.Equal(0, request.Temperature);
        Assert.Equal(GenerationSchema.Json, request.JsonSchema);
        Assert.Equal(PromptFile.Generation.Sha256, request.PromptSha256);
        Assert.Equal(PromptFile.Generation.Text, request.SystemPrompt);
        Assert.NotEqual(PromptFile.Extraction.Sha256, request.PromptSha256);
    }

    [Fact]
    public void TheUserPromptIsTheSectionAndTheQuoteAndNothingElse()
    {
        var request = Generator().RequestFor(AddValueRequirement());

        Assert.Equal($"SECTION: 4.1\nREQUIREMENT: {AddValueQuote}", request.UserPrompt);
    }

    [Fact]
    public void AHardWrappedQuoteIsSentWithItsWhitespaceNormalised()
    {
        var wrapped = Corpus.Verifier().Verify(
        [
            new CandidateRequirement(
                Modality.Must,
                AddValueQuote.Replace(" member ", "\n   member  ", StringComparison.Ordinal),
                Testability.Testable,
                null),
        ]).Register.Single();

        Assert.Equal($"SECTION: 4.1\nREQUIREMENT: {AddValueQuote}", Generator().RequestFor(wrapped).UserPrompt);
    }

    [Fact]
    public void TheUserPromptCarriesNeitherTheRequirementIdNorAnyOfTheDocumentBeyondTheQuote()
    {
        var requirement = AddValueRequirement();
        var request = Generator().RequestFor(requirement);

        Assert.DoesNotContain(requirement.Id, request.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("REQ-", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("Request for Comments: 6902", Corpus.Raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Request for Comments: 6902", request.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("DOCUMENT ID", request.UserPrompt, StringComparison.Ordinal);
    }

    private static TestCaseGenerator Generator() =>
        new(new ScriptedLlmClient("{}"), LlmClientFactory.DefaultModel);

    private static Requirement AddValueRequirement() =>
        Corpus.Verifier().Verify(
        [
            new CandidateRequirement(Modality.Must, AddValueQuote, Testability.Testable, null),
        ]).Register.Single();
}
