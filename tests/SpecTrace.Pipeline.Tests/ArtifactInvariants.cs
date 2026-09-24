using System.Text.Json;
using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

internal static class ArtifactInvariants
{
    public const string HumanDecisionLogFile = "reviews.jsonl";

    public static RunFiles Read(string runDirectory) =>
        new(
            Load(runDirectory, RunArtifacts.RequirementsFile).EnumerateArray().ToList(),
            Load(runDirectory, RunArtifacts.RejectedQuotesFile).EnumerateArray().ToList(),
            Load(runDirectory, RunArtifacts.DecisionsFile).EnumerateArray().ToList(),
            Load(runDirectory, RunArtifacts.TestCasesFile).EnumerateArray().ToList(),
            ReadMatrix(runDirectory));

    public static JsonElement ReadMatrix(string runDirectory) => Load(runDirectory, RunArtifacts.MatrixFile);

    public static List<string> RequirementIds(string runDirectory) =>
        Read(runDirectory).Requirements.Select(Text("id")).ToList();

    public static void AssertHold(string runDirectory, string rawDocument)
    {
        var files = Read(runDirectory);
        var rows = files.Matrix.GetProperty("rows").EnumerateArray().ToList();

        AssertI6IdsAreUniqueWithinTheRun(files);

        var requirements = files.Requirements.ToDictionary(Text("id"), StringComparer.Ordinal);

        AssertI1SpansNormaliseToTheirText(files, rawDocument);
        AssertI2CasesNameOnlyRequirementsInTheRegister(files, requirements);
        AssertI3EveryRequirementAppearsInTheMatrixExactlyOnce(requirements, rows);
        AssertI4GapIfAndOnlyIfNoCaseAndNoHumanDecision(runDirectory, files, requirements, rows);
        AssertI5NoCaseHasAnEmptyRequirementList(files);
        AssertI7NoFailedVerificationReachesTheRegisterOrTheMatrix(files, requirements, rows);
    }

    private static void AssertI1SpansNormaliseToTheirText(RunFiles files, string rawDocument)
    {
        foreach (var requirement in files.Requirements)
        {
            var span = requirement.GetProperty("span");
            var raw = rawDocument[span.GetProperty("start").GetInt32()..span.GetProperty("end").GetInt32()];

            Assert.True(
                TextNormalizer.Normalize(Text("text")(requirement)) == TextNormalizer.Normalize(raw),
                $"I1: the raw text at {Text("id")(requirement)}'s span does not normalise to its text.");
        }
    }

    private static void AssertI2CasesNameOnlyRequirementsInTheRegister(
        RunFiles files,
        Dictionary<string, JsonElement> requirements)
    {
        foreach (var testCase in files.TestCases)
        {
            foreach (var requirementId in Strings(testCase.GetProperty("requirement_ids")))
            {
                Assert.True(
                    requirements.ContainsKey(requirementId),
                    $"I2: {Text("id")(testCase)} names {requirementId}, which is not in the register.");
                Assert.True(
                    requirements[requirementId].GetProperty("testability").GetString() == "testable",
                    $"REQ-GEN-01: {Text("id")(testCase)} was generated for {requirementId}, which is not testable.");
            }
        }

        Assert.True(
            files.Matrix.GetProperty("orphans").GetArrayLength() == 0,
            "I2: the matrix lists orphan cases, so some case names a requirement that is not in the register.");
    }

    private static void AssertI3EveryRequirementAppearsInTheMatrixExactlyOnce(
        Dictionary<string, JsonElement> requirements,
        List<JsonElement> rows)
    {
        var rowIds = rows.Select(Text("requirement_id")).ToList();

        Assert.True(
            rowIds.Count == rowIds.Distinct(StringComparer.Ordinal).Count(),
            "I3: a requirement appears in the matrix more than once.");
        Assert.Equal(
            requirements.Keys.Order(StringComparer.Ordinal),
            rowIds.Order(StringComparer.Ordinal));
    }

    private static void AssertI4GapIfAndOnlyIfNoCaseAndNoHumanDecision(
        string runDirectory,
        RunFiles files,
        Dictionary<string, JsonElement> requirements,
        List<JsonElement> rows)
    {
        Assert.False(
            File.Exists(Path.Combine(runDirectory, HumanDecisionLogFile)),
            $"I4: this check assumes the run holds no logged human decision, but {HumanDecisionLogFile} exists. "
            + "Read the decisions from it before relaxing this.");

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
                $"I4: {requirementId} is '{status}', but no human decision is logged in this run, and only a "
                + "human decision may mark a requirement not testable or deferred.");
            Assert.True(
                (status == "gap") == (nonRejected.Count == 0),
                $"I4: {requirementId} is '{status}' with {nonRejected.Count} non-rejected cases and no human decision.");
            Assert.Equal(nonRejected, Strings(row.GetProperty("test_case_ids")));

            if (requirements[requirementId].GetProperty("testability").GetString() != "testable")
            {
                Assert.True(
                    status == "gap",
                    $"I4: the model flagged {requirementId}, no human has decided it, so it must stay a gap; it is '{status}'.");
            }
        }
    }

    private static void AssertI5NoCaseHasAnEmptyRequirementList(RunFiles files)
    {
        foreach (var testCase in files.TestCases)
        {
            Assert.True(
                testCase.GetProperty("requirement_ids").GetArrayLength() > 0,
                $"I5: {Text("id")(testCase)} names no requirement.");
        }
    }

    private static void AssertI6IdsAreUniqueWithinTheRun(RunFiles files)
    {
        var ids = files.Requirements.Select(Text("id")).ToList();

        Assert.True(
            ids.Count == ids.Distinct(StringComparer.Ordinal).Count(),
            "I6: a requirement ID appears more than once in the register.");
    }

    private static void AssertI7NoFailedVerificationReachesTheRegisterOrTheMatrix(
        RunFiles files,
        Dictionary<string, JsonElement> requirements,
        List<JsonElement> rows)
    {
        foreach (var requirement in files.Requirements)
        {
            Assert.True(
                Text("verification")(requirement) == "exact",
                $"I7: {Text("id")(requirement)} is in the register with verification '{Text("verification")(requirement)}'.");
        }

        Assert.All(rows, row => Assert.True(
            requirements.ContainsKey(Text("requirement_id")(row)),
            $"I7: the matrix has a row for {Text("requirement_id")(row)}, which is not a verified requirement."));

        var registerTexts = files.Requirements
            .Select(requirement => TextNormalizer.Normalize(Text("text")(requirement)))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var rejected in files.Rejected)
        {
            Assert.False(
                registerTexts.Contains(TextNormalizer.Normalize(Text("quote")(rejected))),
                $"I7: the rejected quote '{Text("quote")(rejected)}' is in the register.");
        }

        foreach (var decision in files.Decisions.Where(decision => Text("verification")(decision) == "ambiguous"))
        {
            Assert.False(
                registerTexts.Contains(TextNormalizer.Normalize(Text("quote")(decision))),
                $"I7: the ambiguous quote '{Text("quote")(decision)}' is in the register.");
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
