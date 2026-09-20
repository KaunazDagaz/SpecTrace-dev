using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

public sealed class CoreDoesNotDependOnLlmTests
{
    private const string LlmAssemblyName = "SpecTrace.Llm";

    [Fact]
    public void TheCoreProjectFileDeclaresNoDependencyOnTheLlmProject()
    {
        var projectFile = Path.Combine(
            RepositoryRoot(), "src", "SpecTrace.Core", "SpecTrace.Core.csproj");

        var references = File.ReadAllLines(projectFile)
            .Where(line => line.Contains("Reference", StringComparison.Ordinal))
            .Where(line => line.Contains(LlmAssemblyName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(references);
    }

    [Fact]
    public void TheCompiledCoreAssemblyReferencesNoLlmAssembly()
    {
        var referenced = typeof(TextSpan).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty);

        Assert.DoesNotContain(LlmAssemblyName, referenced);
    }

    [Fact]
    public void TheLlmAssemblyIsPresentSoTheCheckIsNotVacuous()
    {
        var llmAssembly = Path.Combine(AppContext.BaseDirectory, $"{LlmAssemblyName}.dll");

        Assert.True(File.Exists(llmAssembly), $"Expected '{llmAssembly}' to exist.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpecTrace.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find SpecTrace.sln above '{AppContext.BaseDirectory}'.");
    }
}
