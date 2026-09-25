using System.Globalization;
using SpecTrace.Core;
using SpecTrace.Llm;
using SpecTrace.Pipeline;

namespace SpecTrace.Cli;

public static class SpecTraceCli
{
    public const int ExitSuccess = 0;
    public const int ExitFailure = 1;
    public const int ExitUsage = 64;

    public const string OfflineFlag = "--offline";

    public static async Task<int> RunAsync(string[] args, CliHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(host);

        var version = typeof(SpecTraceCli).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        host.Out.WriteLine($"spectrace {version}");
        host.Out.WriteLine();

        if (args.Length == 0)
        {
            WriteUsage(host.Out);
            return ExitSuccess;
        }

        switch (args[0])
        {
            case "extract":
                return await ExtractAsync(args[1..], host, cancellationToken).ConfigureAwait(false);

            case "run":
                return await RunCommandAsync(args[1..], host, cancellationToken).ConfigureAwait(false);

            default:
                host.Error.WriteLine($"'{args[0]}' is not implemented yet.");
                host.Error.WriteLine();
                WriteUsage(host.Error);
                return ExitUsage;
        }
    }

    private static Task<int> ExtractAsync(string[] args, CliHost host, CancellationToken cancellationToken) =>
        ExecuteAsync("extract", args, host, async (invocation, token) =>
        {
            var result = await ExtractionRun
                .ExecuteAsync(invocation.DocumentPath, invocation.Client, invocation.Model, host.Prompts.Extraction, token)
                .ConfigureAwait(false);

            var outputDirectory = invocation.OutputDirectory ?? Path.Combine("runs", result.RunId);

            await RunArtifacts
                .WriteAsync(result.Outcome, outputDirectory, token)
                .ConfigureAwait(false);

            WriteSummary(host.Out, result, invocation, outputDirectory);
        }, cancellationToken);

    private static Task<int> RunCommandAsync(string[] args, CliHost host, CancellationToken cancellationToken) =>
        ExecuteAsync("run", args, host, async (invocation, token) =>
        {
            var result = await PipelineRun
                .ExecuteAsync(invocation.DocumentPath, invocation.Client, invocation.Model, host.Prompts, TimeProvider.System, token)
                .ConfigureAwait(false);

            var outputDirectory = invocation.OutputDirectory ?? Path.Combine("runs", result.RunId);

            await RunArtifacts
                .WriteRunAsync(result, outputDirectory, token)
                .ConfigureAwait(false);

            WriteRunSummary(host.Out, result, invocation, outputDirectory);
        }, cancellationToken);

    private static async Task<int> ExecuteAsync(
        string command,
        string[] args,
        CliHost host,
        Func<Invocation, CancellationToken, Task> body,
        CancellationToken cancellationToken)
    {
        if (!TryParse(args, host.Error, out var values, out var flags))
        {
            return ExitUsage;
        }

        if (!values.TryGetValue("--document", out var documentPath))
        {
            host.Error.WriteLine($"{command} needs --document <path>.");
            return ExitUsage;
        }

        var model = values.GetValueOrDefault("--model", LlmClientFactory.DefaultModel);
        var cacheDirectory = values.GetValueOrDefault("--cache", "cache");
        var offline = flags.Contains(OfflineFlag)
            || CachingLlmClient.IsOffline(host.Environment(CachingLlmClient.OfflineVariable));

        using var httpClient = new HttpClient(host.Network, disposeHandler: false)
        {
            Timeout = TimeSpan.FromMinutes(3),
        };

        try
        {
            var client = LlmClientFactory.Create(
                httpClient,
                cacheDirectory,
                offline,
                GeminiLlmClient.ApiKeyFrom(host.Environment(GeminiLlmClient.ApiKeyVariable)));

            var invocation = new Invocation(documentPath, client, model, values.GetValueOrDefault("--out"), offline);

            await body(invocation, cancellationToken).ConfigureAwait(false);
            return ExitSuccess;
        }
        catch (LlmException exception)
        {
            WriteFailure(host.Error, exception.Message);
            return ExitFailure;
        }
        catch (InvalidOperationException exception)
        {
            WriteFailure(host.Error, exception.Message);
            return ExitFailure;
        }
    }

    private static void WriteFailure(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("No run artifacts were written.");
    }

    private static bool TryParse(
        string[] args,
        TextWriter error,
        out Dictionary<string, string> values,
        out HashSet<string> flags)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        flags = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == OfflineFlag)
            {
                flags.Add(args[i]);
                continue;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error.WriteLine($"{args[i]} needs a value.");
                return false;
            }

            values[args[i]] = args[++i];
        }

        return true;
    }

    private static void WriteSummary(
        TextWriter output,
        ExtractionRunResult result,
        Invocation invocation,
        string outputDirectory)
    {
        var outcome = result.Outcome;

        output.WriteLine($"document       {result.DocumentId}");
        output.WriteLine($"model          {invocation.Model}");
        output.WriteLine($"mode           {(invocation.Offline ? "offline" : "online")}");
        output.WriteLine($"response       {(result.Response.FromCache ? "from cache" : "from provider")}");
        output.WriteLine($"tokens         {result.Response.InputTokens} in, {result.Response.OutputTokens} out");
        output.WriteLine();
        output.WriteLine($"quotes         {outcome.ClaimCount} returned by the model");
        output.WriteLine($"located        {outcome.ExactClaimCount} exactly once");
        output.WriteLine($"ambiguous      {outcome.AmbiguousClaimCount} found more than once");
        output.WriteLine($"not found      {outcome.Rejected.Count}");
        output.WriteLine(
            $"verification   {outcome.VerificationRate.ToString("P1", CultureInfo.InvariantCulture)} "
            + "of returned quotes located exactly once");
        output.WriteLine();
        output.WriteLine($"register       {outcome.Register.Count} requirements");
        output.WriteLine($"decisions      {outcome.Decisions.Count} for a person");
        output.WriteLine();
        output.WriteLine($"written to     {outputDirectory}");

        output.WriteLine();
        output.WriteLine(
            "The register holds only claims located in the source. It is not a statement that "
            + "every requirement in the document was found.");
    }

    private static void WriteRunSummary(
        TextWriter output,
        PipelineRunResult result,
        Invocation invocation,
        string outputDirectory)
    {
        var outcome = result.Extraction.Outcome;
        var generations = result.Generations;
        var rows = result.Matrix.Rows;

        output.WriteLine($"document       {result.DocumentId}");
        output.WriteLine($"model          {invocation.Model}");
        output.WriteLine($"mode           {(invocation.Offline ? "offline" : "online")}");
        output.WriteLine(
            $"extraction     {(result.Extraction.Response.FromCache ? "from cache" : "from provider")}, "
            + $"{result.Extraction.Response.InputTokens} tokens in, {result.Extraction.Response.OutputTokens} out");
        output.WriteLine(
            $"generation     {generations.Count} calls, {generations.Count(generation => generation.Response.FromCache)} from cache, "
            + $"{generations.Sum(generation => generation.Response.InputTokens)} tokens in, "
            + $"{generations.Sum(generation => generation.Response.OutputTokens)} out");
        output.WriteLine();
        output.WriteLine($"quotes         {outcome.ClaimCount} returned by the model, {outcome.ExactClaimCount} located exactly once, "
            + $"{outcome.AmbiguousClaimCount} ambiguous, {outcome.Rejected.Count} not found");
        output.WriteLine($"register       {result.Register.Count} requirements");
        output.WriteLine($"covered        {rows.Count(row => row.Status == CoverageStatus.Covered)} by at least one proposed case");
        output.WriteLine($"gaps           {rows.Count(row => row.Status == CoverageStatus.Gap)}");
        output.WriteLine($"blocked        {generations.Count(generation => generation.BlockedReason is not null)} generations returned no case");
        output.WriteLine($"orphans        {result.Matrix.Orphans.Count}");
        output.WriteLine();
        output.WriteLine(
            $"test cases     {result.Cases.Count}, of which "
            + $"{result.Cases.Count(testCase => testCase.Status == ReviewStatus.Proposed)} not yet reviewed by a person");

        foreach (var type in Enum.GetValues<CaseType>())
        {
            output.WriteLine($"  {RunArtifacts.Spell(type),-12} {result.Cases.Count(testCase => testCase.Type == type)}");
        }

        output.WriteLine();
        output.WriteLine($"decision queue {result.DecisionQueue.Count} items for a person");

        foreach (var reason in result.DecisionQueue.GroupBy(decision => decision.Reason).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"  {reason.Key,-44} {reason.Count()}");
        }

        output.WriteLine();
        output.WriteLine($"written to     {outputDirectory}");
        output.WriteLine();
        output.WriteLine(
            "Coverage is by proposed, unreviewed test cases. The matrix does not claim the specification "
            + "is fully covered: it shows only whether each requirement in the register has a proposed case.");
    }

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage: spectrace <command> [options]");
        output.WriteLine();
        output.WriteLine("Commands:");
        output.WriteLine("  run     --document <path> [--offline] [--model <id>] [--cache <dir>] [--out <dir>]");
        output.WriteLine("          extract, verify, generate test cases, assemble the matrix and decision queue");
        output.WriteLine("  extract --document <path> [--offline] [--model <id>] [--cache <dir>] [--out <dir>]");
        output.WriteLine("          ask the model for requirements and keep only those located in the source");
        output.WriteLine();
        output.WriteLine("Not implemented yet — each lands with its own task:");
        output.WriteLine("  score   --run <id> --gold <path>  score a run against a gold standard");
        output.WriteLine("  export  --run <id>                export the matrix");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine($"  {OfflineFlag}          replay from cache only; a miss is an error. Same as {CachingLlmClient.OfflineVariable}=1");
        output.WriteLine();
        output.WriteLine("Environment:");
        output.WriteLine($"  {GeminiLlmClient.ApiKeyVariable}     provider key; never written to any file");
        output.WriteLine($"  {CachingLlmClient.OfflineVariable}  1 = replay from cache only; a miss is an error");
        output.WriteLine();
        output.WriteLine("Concept, requirements and plan: https://github.com/KaunazDagaz/SpecTrace-docs");
    }

    private sealed record Invocation(
        string DocumentPath,
        ILlmClient Client,
        string Model,
        string? OutputDirectory,
        bool Offline);
}
