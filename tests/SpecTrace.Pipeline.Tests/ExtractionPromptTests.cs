using System.Text.Json;

namespace SpecTrace.Pipeline.Tests;

public sealed class ExtractionPromptTests
{
    private static readonly string[] PositionLikeNames =
    [
        "offset", "position", "location", "line", "column", "index", "start", "end", "span", "char",
    ];

    [Fact]
    public void TheResponseSchemaDeclaresNoFieldThatCouldCarryAPosition()
    {
        using var schema = JsonDocument.Parse(ExtractionSchema.Json);
        var names = PropertyNamesIn(schema.RootElement).ToList();

        Assert.NotEmpty(names);

        foreach (var name in names)
        {
            foreach (var forbidden in PositionLikeNames)
            {
                Assert.False(
                    name.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"Schema property '{name}' looks like it could carry a position ('{forbidden}').");
            }
        }
    }

    [Fact]
    public void TheResponseSchemaDeclaresExactlyTheFourFieldsOfPlanSection71()
    {
        using var schema = JsonDocument.Parse(ExtractionSchema.Json);
        var properties = schema.RootElement
            .GetProperty("items")
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["modality", "quote", "testability", "testability_note"], properties);
    }

    [Fact]
    public void TheOutputSchemaWrittenInThePromptDeclaresTheSameFieldsAndNoPosition()
    {
        var text = PromptFile.Extraction.Text;
        var declared = text[text.IndexOf("Output schema:", StringComparison.Ordinal)..];

        foreach (var field in new[] { "\"modality\"", "\"quote\"", "\"testability\"", "\"testability_note\"" })
        {
            Assert.Contains(field, declared, StringComparison.Ordinal);
        }

        foreach (var forbidden in PositionLikeNames)
        {
            Assert.DoesNotContain($"\"{forbidden}", declared, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ThePromptTellsTheModelItDoesNotKnowPositionsAndMustNotReportThem()
    {
        Assert.Contains(
            "Never report character positions, line numbers or offsets.",
            PromptFile.Extraction.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ThePromptHashDoesNotDependOnTheLineEndingsOfTheCheckout()
    {
        var lf = PromptFile.Extraction.Text;
        var crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);

        Assert.DoesNotContain('\r', lf);
        Assert.Equal(Sha256(lf), PromptFile.Extraction.Sha256);

        Assert.NotEqual(Sha256(crlf), PromptFile.Extraction.Sha256);
    }

    [Fact]
    public void EveryExtractionRequestIsAtTemperatureZeroWithTheSchemaAndThePromptHash()
    {
        var request = new RequirementExtractor(new ScriptedLlmClient("[]"), "gemini-3.5-flash")
            .RequestFor(Corpus.DocumentId, "the document");

        Assert.Equal(0, request.Temperature);
        Assert.Equal(ExtractionSchema.Json, request.JsonSchema);
        Assert.Equal(PromptFile.Extraction.Sha256, request.PromptSha256);
        Assert.Equal(PromptFile.Extraction.Text, request.SystemPrompt);
    }

    [Fact]
    public void TheRawDocumentIsSentWithItsLineStructureIntact()
    {
        var request = new RequirementExtractor(new ScriptedLlmClient("[]"), "gemini-3.5-flash")
            .RequestFor(Corpus.DocumentId, Corpus.Raw);

        Assert.StartsWith("DOCUMENT ID: rfc6902\n\n", request.UserPrompt, StringComparison.Ordinal);
        Assert.EndsWith(Corpus.Raw, request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("\n4.1.  add\n", request.UserPrompt, StringComparison.Ordinal);
    }

    private static IEnumerable<string> PropertyNamesIn(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == "properties")
                {
                    foreach (var declared in property.Value.EnumerateObject())
                    {
                        yield return declared.Name;
                    }
                }

                foreach (var nested in PropertyNamesIn(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in PropertyNamesIn(item))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string Sha256(string text) =>
        Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
}
