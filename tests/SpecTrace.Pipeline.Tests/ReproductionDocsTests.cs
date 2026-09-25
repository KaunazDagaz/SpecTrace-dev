namespace SpecTrace.Pipeline.Tests;

public sealed class ReproductionDocsTests
{
    private static readonly string[] Workflow =
        File.ReadAllLines(Repository.PathTo(".github", "workflows", "ci.yml"));

    [Fact]
    public void TheReadmeReproduceCommandIsTheExactCommandCiRuns()
    {
        var command = ReproduceCommand();

        Assert.StartsWith("dotnet run --project src/SpecTrace.Cli -- run ", command, StringComparison.Ordinal);
        Assert.Contains(" --offline", command, StringComparison.Ordinal);
        Assert.Contains(
            Workflow,
            line => line.Trim() == $"run: {command}" || line.Trim() == $"- run: {command}");
    }

    [Fact]
    public void TheWorkflowReferencesNoSecret()
    {
        Assert.DoesNotContain(
            Workflow,
            line => line.Contains("secrets", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReproduceCommand()
    {
        var readme = File.ReadAllLines(Repository.PathTo("README.md"));
        var heading = Array.FindIndex(readme, line => line.Trim() == "## Reproduce");

        Assert.True(heading >= 0, "README.md has no '## Reproduce' section.");

        var fence = Array.FindIndex(readme, heading, line => line.StartsWith("```", StringComparison.Ordinal));

        Assert.True(fence > heading, "The Reproduce section of README.md has no code block.");

        var command = readme[fence + 1].Trim();

        Assert.True(
            readme[fence + 2].StartsWith("```", StringComparison.Ordinal),
            "The Reproduce code block must hold exactly one command.");

        return command;
    }
}
