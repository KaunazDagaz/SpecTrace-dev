using System.Globalization;
using SpecTrace.Core;
using SpecTrace.Llm;
using SpecTrace.Pipeline;

namespace SpecTrace.Cli;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
    private const int ExitUsage = 64;

    private static async Task<int> Main(string[] args)
    {
        var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        Console.WriteLine($"spectrace {version}");
        Console.WriteLine();

        if (args.Length == 0)
        {
            WriteUsage(Console.Out);
            return ExitSuccess;
        }

        switch (args[0])
        {
            case "extract":
                return await ExtractAsync(args[1..]).ConfigureAwait(false);

            case "run":
                return await RunAsync(args[1..]).ConfigureAwait(false);

            default:
                Console.Error.WriteLine($"'{args[0]}' is not implemented yet.");
                Console.Error.WriteLine();
                WriteUsage(Console.Error);
                return ExitUsage;
        }
    }

    private static Task<int> ExtractAsync(string[] args) =>
        ExecuteAsync("extract", args, async (documentPath, client, model, options, offline, cancellationToken) =>
        {
            var result = await ExtractionRun
                .ExecuteAsync(documentPath, client, model, cancellationToken)
                .ConfigureAwait(false);

            var outputDirectory = options.GetValueOrDefault("--out", Path.Combine("runs", result.RunId));

            await RunArtifacts
                .WriteAsync(result.Outcome, outputDirectory, cancellationToken)
                .ConfigureAwait(false);

            WriteSummary(result, model, offline, outputDirectory);
        });

    private static Task<int> RunAsync(string[] args) =>
        ExecuteAsync("run", args, async (documentPath, client, model, options, offline, cancellationToken) =>
        {
            var result = await PipelineRun
                .ExecuteAsync(documentPath, client, model, cancellationToken)
                .ConfigureAwait(false);

            var outputDirectory = options.GetValueOrDefault("--out", Path.Combine("runs", result.RunId));

            await RunArtifacts
                .WriteRunAsync(result, outputDirectory, cancellationToken)
                .ConfigureAwait(false);

            WriteRunSummary(result, model, offline, outputDirectory);
        });

    private static async Task<int> ExecuteAsync(
        string command,
        string[] args,
        Func<string, ILlmClient, string, Dictionary<string, string>, bool, CancellationToken, Task> body)
    {
        var options = ParseOptions(args);

        if (!options.TryGetValue("--document", out var documentPath))
        {
            Console.Error.WriteLine($"{command} needs --document <path>.");
            return ExitUsage;
        }

        var model = options.GetValueOrDefault("--model", LlmClientFactory.DefaultModel);
        var cacheDirectory = options.GetValueOrDefault("--cache", "cache");
        var offline = CachingLlmClient.OfflineFromEnvironment();

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        try
        {
            var client = LlmClientFactory.Create(
                httpClient,
                cacheDirectory,
                offline,
                GeminiLlmClient.ApiKeyFromEnvironment());

            await body(documentPath, client, model, options, offline, cancellation.Token).ConfigureAwait(false);
            return ExitSuccess;
        }
        catch (LlmException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitFailure;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitFailure;
        }
    }

    private static void WriteSummary(
        ExtractionRunResult result,
        string model,
        bool offline,
        string outputDirectory)
    {
        var outcome = result.Outcome;

        Console.WriteLine($"document       {result.DocumentId}");
        Console.WriteLine($"model          {model}");
        Console.WriteLine($"mode           {(offline ? "offline" : "online")}");
        Console.WriteLine($"response       {(result.Response.FromCache ? "from cache" : "from provider")}");
        Console.WriteLine($"tokens         {result.Response.InputTokens} in, {result.Response.OutputTokens} out");
        Console.WriteLine();
        Console.WriteLine($"quotes         {outcome.ClaimCount} returned by the model");
        Console.WriteLine($"located        {outcome.ExactClaimCount} exactly once");
        Console.WriteLine($"ambiguous      {outcome.AmbiguousClaimCount} found more than once");
        Console.WriteLine($"not found      {outcome.Rejected.Count}");
        Console.WriteLine(
            $"verification   {outcome.VerificationRate.ToString("P1", CultureInfo.InvariantCulture)} "
            + "of returned quotes located exactly once");
        Console.WriteLine();
        Console.WriteLine($"register       {outcome.Register.Count} requirements");
        Console.WriteLine($"decisions      {outcome.Decisions.Count} for a person");
        Console.WriteLine();
        Console.WriteLine($"written to     {outputDirectory}");

        Console.WriteLine();
        Console.WriteLine(
            "The register holds only claims located in the source. It is not a statement that "
            + "every requirement in the document was found.");
    }

    private static void WriteRunSummary(
        PipelineRunResult result,
        string model,
        bool offline,
        string outputDirectory)
    {
        var outcome = result.Extraction.Outcome;
        var generations = result.Generations;
        var rows = result.Matrix.Rows;

        Console.WriteLine($"document       {result.DocumentId}");
        Console.WriteLine($"model          {model}");
        Console.WriteLine($"mode           {(offline ? "offline" : "online")}");
        Console.WriteLine(
            $"extraction     {(result.Extraction.Response.FromCache ? "from cache" : "from provider")}, "
            + $"{result.Extraction.Response.InputTokens} tokens in, {result.Extraction.Response.OutputTokens} out");
        Console.WriteLine(
            $"generation     {generations.Count} calls, {generations.Count(generation => generation.Response.FromCache)} from cache, "
            + $"{generations.Sum(generation => generation.Response.InputTokens)} tokens in, "
            + $"{generations.Sum(generation => generation.Response.OutputTokens)} out");
        Console.WriteLine();
        Console.WriteLine($"quotes         {outcome.ClaimCount} returned by the model, {outcome.ExactClaimCount} located exactly once, "
            + $"{outcome.AmbiguousClaimCount} ambiguous, {outcome.Rejected.Count} not found");
        Console.WriteLine($"register       {result.Register.Count} requirements");
        Console.WriteLine($"covered        {rows.Count(row => row.Status == CoverageStatus.Covered)} by at least one proposed case");
        Console.WriteLine($"gaps           {rows.Count(row => row.Status == CoverageStatus.Gap)}");
        Console.WriteLine($"blocked        {generations.Count(generation => generation.BlockedReason is not null)} generations returned no case");
        Console.WriteLine($"orphans        {result.Matrix.Orphans.Count}");
        Console.WriteLine();
        Console.WriteLine(
            $"test cases     {result.Cases.Count}, of which "
            + $"{result.Cases.Count(testCase => testCase.Status == ReviewStatus.Proposed)} not yet reviewed by a person");

        foreach (var type in Enum.GetValues<CaseType>())
        {
            Console.WriteLine($"  {RunArtifacts.Spell(type),-12} {result.Cases.Count(testCase => testCase.Type == type)}");
        }

        Console.WriteLine();
        Console.WriteLine($"decision queue {result.DecisionQueue.Count} items for a person");

        foreach (var reason in result.DecisionQueue.GroupBy(decision => decision.Reason).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {reason.Key,-44} {reason.Count()}");
        }

        Console.WriteLine();
        Console.WriteLine($"written to     {outputDirectory}");
        Console.WriteLine();
        Console.WriteLine(
            "Coverage is by proposed, unreviewed test cases. The matrix does not claim the specification "
            + "is fully covered: it shows only whether each requirement in the register has a proposed case.");
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            options[args[i]] = args[i + 1];
        }

        return options;
    }

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage: spectrace <command> [options]");
        output.WriteLine();
        output.WriteLine("Commands:");
        output.WriteLine("  run     --document <path> [--model <id>] [--cache <dir>] [--out <dir>]");
        output.WriteLine("          extract, verify, generate test cases, assemble the matrix and decision queue");
        output.WriteLine("  extract --document <path> [--model <id>] [--cache <dir>] [--out <dir>]");
        output.WriteLine("          ask the model for requirements and keep only those located in the source");
        output.WriteLine();
        output.WriteLine("Not implemented yet — each lands with its own task:");
        output.WriteLine("  score   --run <id> --gold <path>  score a run against a gold standard");
        output.WriteLine("  export  --run <id>                export the matrix");
        output.WriteLine();
        output.WriteLine("Environment:");
        output.WriteLine($"  {GeminiLlmClient.ApiKeyVariable}     provider key; never written to any file");
        output.WriteLine($"  {CachingLlmClient.OfflineVariable}  1 = replay from cache only; a miss is an error");
        output.WriteLine();
        output.WriteLine("Concept, requirements and plan: https://github.com/KaunazDagaz/SpecTrace-docs");
    }
}
