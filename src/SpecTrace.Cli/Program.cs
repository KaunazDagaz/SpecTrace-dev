using System.Globalization;
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

        if (args[0] == "extract")
        {
            return await ExtractAsync(args[1..]).ConfigureAwait(false);
        }

        Console.Error.WriteLine($"'{args[0]}' is not implemented yet.");
        Console.Error.WriteLine();
        WriteUsage(Console.Error);
        return ExitUsage;
    }

    private static async Task<int> ExtractAsync(string[] args)
    {
        var options = ParseOptions(args);

        if (!options.TryGetValue("--document", out var documentPath))
        {
            Console.Error.WriteLine("extract needs --document <path>.");
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

            var result = await ExtractionRun
                .ExecuteAsync(documentPath, client, model, cancellation.Token)
                .ConfigureAwait(false);

            var outputDirectory = options.GetValueOrDefault("--out", Path.Combine("runs", result.RunId));

            await RunArtifacts
                .WriteAsync(result.Outcome, outputDirectory, cancellation.Token)
                .ConfigureAwait(false);

            WriteSummary(result, model, offline, outputDirectory);
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
        Console.WriteLine($"claimed        {result.CandidateCount}");
        Console.WriteLine($"verified       {outcome.Register.Count}");
        Console.WriteLine($"rejected       {outcome.Rejected.Count}");
        Console.WriteLine($"ambiguous      {outcome.Decisions.Count}");
        Console.WriteLine(
            $"verification   {outcome.VerificationRate.ToString("P1", CultureInfo.InvariantCulture)} "
            + "of distinct claims located exactly");
        Console.WriteLine();
        Console.WriteLine($"written to     {outputDirectory}");

        Console.WriteLine();
        Console.WriteLine(
            "The register holds only claims located in the source. It is not a statement that "
            + "every requirement in the document was found.");
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
        output.WriteLine("  extract --document <path> [--model <id>] [--cache <dir>] [--out <dir>]");
        output.WriteLine("          ask the model for requirements and keep only those located in the source");
        output.WriteLine();
        output.WriteLine("Not implemented yet — each lands with its own task:");
        output.WriteLine("  run     --document <path>         extract, verify, generate, assemble the matrix");
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
