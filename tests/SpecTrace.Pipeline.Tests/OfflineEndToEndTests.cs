using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpecTrace.Cli;
using SpecTrace.Llm;
using Xunit.Abstractions;

namespace SpecTrace.Pipeline.Tests;

public sealed class OfflineEndToEndTests : IClassFixture<OfflineEndToEndRuns>
{
    public static readonly string[] ManifestFieldsThatVaryBetweenRuns = ["started_at", "git_sha"];

    private const string Masked = "<varies between runs>";

    private static readonly string ReferenceDirectory = Repository.PathTo("runs", "reference");

    private readonly OfflineEndToEndRuns _runs;
    private readonly ITestOutputHelper _output;

    public OfflineEndToEndTests(OfflineEndToEndRuns runs, ITestOutputHelper output)
    {
        _runs = runs;
        _output = output;
    }

    [Fact]
    public void TheRunWithSpectraceOfflineSetAndNoKeyCompletesWithoutASingleNetworkRequest()
    {
        var run = _runs.WithVariable;

        Assert.True(run.ExitCode == SpecTraceCli.ExitSuccess, run.Error);
        Assert.Empty(run.Network.Attempted);
        Assert.Contains("mode           offline", run.Output, StringComparison.Ordinal);
        AssertEveryCallCameFromTheCache(run.OutputDirectory);
    }

    [Fact]
    public void TheOfflineFlagAloneRunsTheSameWayAsTheEnvironmentVariable()
    {
        var run = _runs.WithFlag;

        Assert.True(run.ExitCode == SpecTraceCli.ExitSuccess, run.Error);
        Assert.Empty(run.Network.Attempted);
        Assert.Contains("mode           offline", run.Output, StringComparison.Ordinal);
        AssertEveryCallCameFromTheCache(run.OutputDirectory);
    }

    [Fact]
    public void EveryArtifactOfTheOfflineRunMatchesTheCommittedReferenceRunByteForByte()
    {
        Assert.True(
            Directory.Exists(ReferenceDirectory),
            $"'{ReferenceDirectory}' does not exist. Regenerate it with the command in README.md, section Reproduce.");

        var referenceFiles = FileNamesIn(ReferenceDirectory);

        Assert.Contains(RunArtifacts.ManifestFile, referenceFiles);
        Assert.Contains(RunArtifacts.MatrixHtmlFile, referenceFiles);

        foreach (var run in new[] { _runs.WithVariable, _runs.WithFlag })
        {
            Assert.Equal(referenceFiles, FileNamesIn(run.OutputDirectory));

            foreach (var file in referenceFiles)
            {
                var expected = File.ReadAllBytes(Path.Combine(ReferenceDirectory, file));
                var actual = File.ReadAllBytes(Path.Combine(run.OutputDirectory, file));

                if (file == RunArtifacts.ManifestFile)
                {
                    Assert.Equal(MaskVaryingFields(expected), MaskVaryingFields(actual));
                    continue;
                }

                if (!expected.AsSpan().SequenceEqual(actual))
                {
                    Assert.Equal(Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(actual));
                    Assert.Fail($"{file} differs from runs/reference/{file} in bytes that decode to the same text.");
                }
            }
        }
    }

    [Fact]
    public void TheFieldsExcludedFromTheComparisonArePresentOnceAndWellFormed()
    {
        foreach (var directory in new[] { ReferenceDirectory, _runs.WithVariable.OutputDirectory, _runs.WithFlag.OutputDirectory })
        {
            var manifest = File.ReadAllBytes(Path.Combine(directory, RunArtifacts.ManifestFile));
            var values = VaryingValues(manifest);

            Assert.True(
                DateTimeOffset.TryParse(values["started_at"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var startedAt)
                && startedAt.Offset == TimeSpan.Zero,
                $"started_at '{values["started_at"]}' in {directory} is not a UTC timestamp.");
            Assert.Matches("^([0-9a-f]{40}|unknown)$", values["git_sha"]);
        }
    }

    [Fact]
    public void InvariantsI1ToI8HoldOnTheRegeneratedRun()
    {
        foreach (var run in new[] { _runs.WithVariable, _runs.WithFlag })
        {
            ArtifactInvariants.AssertHold(run.OutputDirectory, File.ReadAllText(Repository.PathTo("corpus", "rfc6902.txt")));
        }

        CoreIsolation.AssertTheCoreProjectFileDeclaresNoLlmDependency();
        CoreIsolation.AssertTheCompiledCoreAssemblyReferencesNoLlmAssembly();
    }

    [Fact]
    public void I6RequirementIdsAreIdenticalAcrossTwoRunsAndUniqueWithinEach()
    {
        var first = ArtifactInvariants.RequirementIds(_runs.WithVariable.OutputDirectory);
        var second = ArtifactInvariants.RequirementIds(_runs.WithFlag.OutputDirectory);

        Assert.NotEmpty(first);
        Assert.Equal(first.Count, first.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(second.Count, second.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(first, second);
        Assert.Equal(ArtifactInvariants.RequirementIds(ReferenceDirectory), first);
    }

    [Theory]
    [InlineData("extract.system.md", "the extraction call on rfc6902")]
    [InlineData("generate.system.md", "the generation call for REQ-rfc6902-")]
    public async Task AChangedPromptFileMakesTheOfflineRunFailWithACacheMissThatNamesIt(string promptFile, string call)
    {
        var original = await File.ReadAllTextAsync(
            Repository.PathTo("src", "SpecTrace.Pipeline", "Prompts", promptFile),
            CancellationToken.None);
        var firstLineEnd = original.IndexOf('\n', StringComparison.Ordinal);
        var edited = $"{original[..firstLineEnd]} Be precise.{original[firstLineEnd..]}";

        using var unchangedContent = new MemoryStream(Encoding.UTF8.GetBytes(original));
        using var changedContent = new MemoryStream(Encoding.UTF8.GetBytes(edited));
        var unchanged = PromptFile.Read(promptFile, unchangedContent);
        var changed = PromptFile.Read(promptFile, changedContent);
        var isExtraction = promptFile == PromptFile.Extraction.Name;
        var embedded = isExtraction ? PromptFile.Extraction : PromptFile.Generation;
        var prompts = isExtraction
            ? PromptSet.Embedded with { Extraction = changed }
            : PromptSet.Embedded with { Generation = changed };

        Assert.Equal(embedded.Sha256, unchanged.Sha256);
        Assert.NotEqual(embedded.Sha256, changed.Sha256);

        using var run = await CliRun.ExecuteAsync(OfflineEndToEndRuns.OfflineByVariable, prompts);

        _output.WriteLine($"exit code {run.ExitCode}");
        _output.WriteLine(run.Error);

        Assert.Equal(SpecTraceCli.ExitFailure, run.ExitCode);
        Assert.Empty(run.Network.Attempted);
        Assert.False(Directory.Exists(run.OutputDirectory), "A failed offline run left output behind.");
        Assert.DoesNotContain("written to", run.Output, StringComparison.Ordinal);

        Assert.Contains($"Offline replay has no recorded response for {call}", run.Error, StringComparison.Ordinal);
        Assert.Contains($"prompt {promptFile}", run.Error, StringComparison.Ordinal);
        Assert.Contains($"prompt sha256  {changed.Sha256}", run.Error, StringComparison.Ordinal);

        var key = Regex.Match(run.Error, "cache key +([0-9a-f]{64})").Groups[1].Value;
        var expectedEntry = Repository.PathTo("cache", $"{key}.json");

        Assert.NotEmpty(key);
        Assert.Contains($"expected at    {expectedEntry}", run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(expectedEntry), $"The committed cache already holds {key}.");

        if (isExtraction)
        {
            var request = new RequirementExtractor(new ScriptedLlmClient("[]"), LlmClientFactory.DefaultModel, prompt: changed)
                .RequestFor(Corpus.DocumentId, await File.ReadAllTextAsync(Repository.PathTo("corpus", "rfc6902.txt"), CancellationToken.None));

            Assert.Equal(CacheKey.For(request), key);
        }
    }

    private static void AssertEveryCallCameFromTheCache(string runDirectory)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, RunArtifacts.ManifestFile)));
        var root = manifest.RootElement;

        Assert.True(root.GetProperty("prompt_count").GetInt32() > 0);
        Assert.Equal(root.GetProperty("prompt_count").GetInt32(), root.GetProperty("cache_hits").GetInt32());
    }

    private static List<string> FileNamesIn(string directory) =>
        Directory.EnumerateFileSystemEntries(directory)
            .Select(entry => Path.GetFileName(entry))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string MaskVaryingFields(byte[] manifest)
    {
        var text = Encoding.UTF8.GetString(manifest);

        foreach (var field in ManifestFieldsThatVaryBetweenRuns)
        {
            text = FieldPattern(field).Replace(text, $"\"{field}\": \"{Masked}\"");
        }

        return text;
    }

    private static Dictionary<string, string> VaryingValues(byte[] manifest)
    {
        var text = Encoding.UTF8.GetString(manifest);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in ManifestFieldsThatVaryBetweenRuns)
        {
            var matches = FieldPattern(field).Matches(text);

            Assert.True(matches.Count == 1, $"'{field}' occurs {matches.Count} times in manifest.json; expected exactly once.");
            values[field] = matches[0].Groups["value"].Value;
        }

        return values;
    }

    private static Regex FieldPattern(string field) =>
        new($"\"{Regex.Escape(field)}\": \"(?<value>[^\"]*)\"", RegexOptions.CultureInvariant);
}

public sealed class OfflineEndToEndRuns : IAsyncLifetime
{
    public static readonly IReadOnlyDictionary<string, string> OfflineByVariable =
        new Dictionary<string, string>(StringComparer.Ordinal) { [CachingLlmClient.OfflineVariable] = "1" };

    private static readonly IReadOnlyDictionary<string, string> NothingSet =
        new Dictionary<string, string>(StringComparer.Ordinal);

    internal CliRun WithVariable { get; private set; } = null!;

    internal CliRun WithFlag { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        WithVariable = await CliRun.ExecuteAsync(OfflineByVariable, PromptSet.Embedded);
        WithFlag = await CliRun.ExecuteAsync(NothingSet, PromptSet.Embedded, SpecTraceCli.OfflineFlag);
    }

    public Task DisposeAsync()
    {
        WithVariable?.Dispose();
        WithFlag?.Dispose();
        return Task.CompletedTask;
    }
}

internal sealed class CliRun : IDisposable
{
    private readonly ScratchDirectory _scratch;

    private CliRun(ScratchDirectory scratch, string outputDirectory, int exitCode, NoNetworkHandler network, string output, string error)
    {
        _scratch = scratch;
        OutputDirectory = outputDirectory;
        ExitCode = exitCode;
        Network = network;
        Output = output;
        Error = error;
    }

    public string OutputDirectory { get; }

    public int ExitCode { get; }

    public NoNetworkHandler Network { get; }

    public string Output { get; }

    public string Error { get; }

    public static async Task<CliRun> ExecuteAsync(
        IReadOnlyDictionary<string, string> environment,
        PromptSet prompts,
        params string[] extraArguments)
    {
        var scratch = new ScratchDirectory();
        var outputDirectory = Path.Combine(scratch.Path, "run");
        var network = new NoNetworkHandler();
        using var output = new StringWriter();
        using var error = new StringWriter();

        string[] arguments =
        [
            "run",
            "--document", Repository.PathTo("corpus", "rfc6902.txt"),
            "--cache", Repository.PathTo("cache"),
            "--out", outputDirectory,
            .. extraArguments,
        ];

        var host = new CliHost(network, name => environment.GetValueOrDefault(name), prompts, output, error);
        var exitCode = await SpecTraceCli.RunAsync(arguments, host, CancellationToken.None);

        return new CliRun(scratch, outputDirectory, exitCode, network, output.ToString(), error.ToString());
    }

    public void Dispose() => _scratch.Dispose();
}
