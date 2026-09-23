using System.Text.Json;
using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

internal static class ArtifactInvariants
{
    public static RunFiles Read(string runDirectory) =>
        new(
            Load(runDirectory, RunArtifacts.RequirementsFile).EnumerateArray().ToList(),
            Load(runDirectory, RunArtifacts.RejectedQuotesFile).EnumerateArray().ToList(),
            Load(runDirectory, RunArtifacts.DecisionsFile).EnumerateArray().ToList(),
            Load(runDirectory, RunArtifacts.TestCasesFile).EnumerateArray().ToList(),
            ReadMatrix(runDirectory));

    public static JsonElement ReadMatrix(string runDirectory) => Load(runDirectory, RunArtifacts.MatrixFile);

    public static void AssertHold(string runDirectory, string rawDocument)
    {
        var files = Read(runDirectory);
        var requirements = files.Requirements.ToDictionary(Text("id"), StringComparer.Ordinal);
        var rows = files.Matrix.GetProperty("rows").EnumerateArray().ToList();

        foreach (var requirement in files.Requirements)
        {
            var span = requirement.GetProperty("span");
            var raw = rawDocument[span.GetProperty("start").GetInt32()..span.GetProperty("end").GetInt32()];

            Assert.Equal(
                TextNormalizer.Normalize(requirement.GetProperty("text").GetString()!),
                TextNormalizer.Normalize(raw));
        }

        foreach (var testCase in files.TestCases)
        {
            var named = Strings(testCase.GetProperty("requirement_ids"));

            Assert.NotEmpty(named);

            foreach (var requirementId in named)
            {
                Assert.True(
                    requirements.ContainsKey(requirementId),
                    $"I2: {Text("id")(testCase)} names {requirementId}, which is not in the register.");
                Assert.Equal("testable", requirements[requirementId].GetProperty("testability").GetString());
            }
        }

        Assert.Empty(files.Matrix.GetProperty("orphans").EnumerateArray());

        Assert.Equal(
            requirements.Keys.Order(StringComparer.Ordinal),
            rows.Select(Text("requirement_id")).Order(StringComparer.Ordinal));
        Assert.Equal(rows.Count, rows.Select(Text("requirement_id")).Distinct(StringComparer.Ordinal).Count());

        foreach (var row in rows)
        {
            var requirementId = Text("requirement_id")(row);
            var nonRejected = files.TestCases
                .Where(testCase => testCase.GetProperty("status").GetString() != "rejected"
                    && Strings(testCase.GetProperty("requirement_ids")).Contains(requirementId))
                .Select(Text("id"))
                .Order(StringComparer.Ordinal)
                .ToList();
            var status = Text("status")(row);

            Assert.True(
                status is "covered" or "gap",
                $"I4: {requirementId} is '{status}', but no human decision exists in this run.");
            Assert.True(
                (status == "gap") == (nonRejected.Count == 0),
                $"I4: {requirementId} is '{status}' with {nonRejected.Count} non-rejected cases.");
            Assert.Equal(nonRejected, Strings(row.GetProperty("test_case_ids")));
        }

        var registerTexts = files.Requirements
            .Select(requirement => TextNormalizer.Normalize(requirement.GetProperty("text").GetString()!))
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(files.Requirements, requirement =>
            Assert.Equal("exact", requirement.GetProperty("verification").GetString()));

        foreach (var rejected in files.Rejected)
        {
            Assert.DoesNotContain(
                TextNormalizer.Normalize(rejected.GetProperty("quote").GetString()!),
                registerTexts);
        }

        foreach (var decision in files.Decisions.Where(decision => Text("verification")(decision) == "ambiguous"))
        {
            Assert.DoesNotContain(TextNormalizer.Normalize(Text("quote")(decision)), registerTexts);
        }
    }

    public static Func<JsonElement, string> Text(string property) =>
        element => element.GetProperty(property).GetString()!;

    public static List<string> Strings(JsonElement array) =>
        array.EnumerateArray().Select(item => item.GetString()!).ToList();

    private static JsonElement Load(string runDirectory, string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, file)));
        return document.RootElement.Clone();
    }
}

internal sealed record RunFiles(
    IReadOnlyList<JsonElement> Requirements,
    IReadOnlyList<JsonElement> Rejected,
    IReadOnlyList<JsonElement> Decisions,
    IReadOnlyList<JsonElement> TestCases,
    JsonElement Matrix);
