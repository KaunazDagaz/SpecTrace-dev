namespace SpecTrace.Pipeline.Tests;

public sealed class CoreDoesNotDependOnLlmTests
{
    [Fact]
    public void TheCoreProjectFileDeclaresNoDependencyOnTheLlmProject()
    {
        CoreIsolation.AssertTheCoreProjectFileDeclaresNoLlmDependency();
    }

    [Fact]
    public void TheCompiledCoreAssemblyReferencesNoLlmAssembly()
    {
        CoreIsolation.AssertTheCompiledCoreAssemblyReferencesNoLlmAssembly();
    }

    [Fact]
    public void TheLlmAssemblyIsPresentSoTheCheckIsNotVacuous()
    {
        var llmAssembly = Path.Combine(AppContext.BaseDirectory, $"{CoreIsolation.LlmAssemblyName}.dll");

        Assert.True(File.Exists(llmAssembly), $"Expected '{llmAssembly}' to exist.");
    }
}
