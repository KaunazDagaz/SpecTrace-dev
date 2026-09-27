using System.Text.Json;
using SpecTrace.Cli;

namespace SpecTrace.Pipeline.Tests;

public sealed class SecondDocumentRunTests
{
    private static readonly string DocumentPath = Repository.PathTo("corpus", "rfc10050.txt");

    [Fact]
    public async Task TheRfc10050RunReplaysOfflineWithoutANetworkRequestAndInvariantsI1ToI8Hold()
    {
        using var scratch = new ScratchDirectory();
        var network = new NoNetworkHandler();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = new CliHost(network, _ => null, PromptSet.Embedded, output, error);

        var exitCode = await SpecTraceCli.RunAsync(
            ["run", "--document", DocumentPath, "--cache", Repository.PathTo("cache"), "--out", scratch.Path, SpecTraceCli.OfflineFlag],
            host,
            CancellationToken.None);

        Assert.True(exitCode == SpecTraceCli.ExitSuccess, error.ToString());
        Assert.Empty(network.Attempted);

        ArtifactInvariants.AssertHold(scratch.Path, await File.ReadAllTextAsync(DocumentPath));

        using var cases = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(scratch.Path, RunArtifacts.TestCasesFile)));

        Assert.Contains(
            cases.RootElement.EnumerateArray(),
            testCase => testCase.GetProperty("type").GetString() == "boundary" && testCase.GetProperty("input").GetString() == string.Empty);
    }
}
