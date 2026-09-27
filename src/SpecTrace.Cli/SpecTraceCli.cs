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

    public const string HeadlineFlag = "--headline";

    public const string PipelineArm = "pipeline";

    public const string ExperimentsDirectory = "experiments";

    public const string TranscriptsDirectory = "experiments/a0";

    public const string HeadlineCommandPrefix = "dotnet run --project src/SpecTrace.Cli -- score --headline --documents ";

    private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan BaselineCallTimeout = TimeSpan.FromMinutes(20);

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

            case "score":
                return await ScoreAsync(args[1..], host, cancellationToken).ConfigureAwait(false);

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
            if (invocation.Arm == BaselineRun.Arm)
            {
                var baseline = await BaselineRun
                    .ExecuteAsync(invocation.DocumentPath, invocation.Client, invocation.Model, host.Prompts.Baseline, TimeProvider.System, token)
                    .ConfigureAwait(false);

                var baselineDirectory = invocation.OutputDirectory ?? Path.Combine("runs", baseline.RunId);

                await RunArtifacts
                    .WriteBaselineAsync(baseline, baselineDirectory, token)
                    .ConfigureAwait(false);

                WriteBaselineSummary(host.Out, baseline, invocation, baselineDirectory);
                return;
            }

            var result = await PipelineRun
                .ExecuteAsync(invocation.DocumentPath, invocation.Client, invocation.Model, host.Prompts, TimeProvider.System, token)
                .ConfigureAwait(false);

            var outputDirectory = invocation.OutputDirectory ?? Path.Combine("runs", result.RunId);

            await RunArtifacts
                .WriteRunAsync(result, outputDirectory, token)
                .ConfigureAwait(false);

            WriteRunSummary(host.Out, result, invocation, outputDirectory);
        }, cancellationToken);

    private static async Task<int> ScoreAsync(string[] args, CliHost host, CancellationToken cancellationToken)
    {
        if (!TryParse(args, host.Error, out var values, out var flags))
        {
            return ExitUsage;
        }

        var output = values.GetValueOrDefault("--out", ExperimentsDirectory);

        try
        {
            if (flags.Contains(HeadlineFlag))
            {
                return await HeadlineAsync(values, output, host, cancellationToken).ConfigureAwait(false);
            }

            if (values.TryGetValue("--claims", out var claims))
            {
                return await ClaimsAsync(claims, values, output, host, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is LlmException
            or InvalidTranscriptException
            or UnparseableAnswerException
            or InvalidOperationException
            or FileNotFoundException
            or DirectoryNotFoundException)
        {
            host.Error.WriteLine(exception.Message);
            host.Error.WriteLine("Nothing was scored.");
            return ExitFailure;
        }

        if (values.ContainsKey("--run") || values.ContainsKey("--gold"))
        {
            host.Error.WriteLine("score --run <id> --gold <path> is not implemented yet. It lands with SPEC-12.");
            return ExitUsage;
        }

        host.Error.WriteLine("score needs --claims <file> --document <path>, or --headline --documents <path,path>.");
        WriteUsage(host.Error);
        return ExitUsage;
    }

    private static async Task<int> ClaimsAsync(
        string claims,
        Dictionary<string, string> values,
        string output,
        CliHost host,
        CancellationToken cancellationToken)
    {
        if (!values.TryGetValue("--document", out var documentPath))
        {
            host.Error.WriteLine("score --claims needs --document <path>.");
            return ExitUsage;
        }

        var score = await Experiment
            .ChatAsync(claims, documentPath, host.Prompts.Baseline, output, cancellationToken)
            .ConfigureAwait(false);
        var written = await ExperimentArtifacts
            .WriteMetricsAsync(score.Metrics, output, cancellationToken)
            .ConfigureAwait(false);

        foreach (var claim in score.Claims)
        {
            host.Out.WriteLine(
                $"{claim.Claim.Ordinal,3}  {RunArtifacts.Spell(claim.Outcome),-21} line {claim.Claim.AnswerLine,-4} "
                + $"{claim.Claim.Quote ?? "(no quote)"}");
        }

        host.Out.WriteLine();
        WriteTally(host.Out, score.Metrics);
        host.Out.WriteLine();
        host.Out.WriteLine($"written to     {written}");
        host.Out.WriteLine();
        host.Out.WriteLine(
            "This arm was captured by hand from a chat interface. Its scoring is reproducible; the answer is not.");

        return ExitSuccess;
    }

    private static async Task<int> HeadlineAsync(
        Dictionary<string, string> values,
        string output,
        CliHost host,
        CancellationToken cancellationToken)
    {
        if (!values.TryGetValue("--documents", out var list))
        {
            host.Error.WriteLine("score --headline needs --documents <path,path>.");
            return ExitUsage;
        }

        var documents = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var model = values.GetValueOrDefault("--model", LlmClientFactory.DefaultModel);

        using var httpClient = new HttpClient(host.Network, disposeHandler: false) { Timeout = CallTimeout };

        var client = LlmClientFactory.Create(
            httpClient,
            values.GetValueOrDefault("--cache", "cache"),
            offline: true,
            apiKey: null);

        var rows = await Experiment
            .MeasureAsync(
                documents,
                values.GetValueOrDefault("--transcripts", TranscriptsDirectory),
                output,
                client,
                model,
                host.Prompts,
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            if (row.Metrics is { } metrics)
            {
                await ExperimentArtifacts.WriteMetricsAsync(metrics, output, cancellationToken).ConfigureAwait(false);
            }
        }

        await ExperimentArtifacts
            .WriteHeadlineAsync(rows, HeadlineCommandPrefix + list, output, cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            host.Out.WriteLine(
                $"{row.DocumentId,-12} {ExperimentArtifacts.Code(row.Arm),-3} {ExperimentArtifacts.Name(row.Arm),-9} "
                + (row.Metrics is { } metrics
                    ? $"{metrics.Claimed.NotLocated} of {metrics.Claimed.Claims} claims not located"
                    : $"not scored: {row.NotScored}"));
        }

        host.Out.WriteLine();
        host.Out.WriteLine("mode           offline, replayed from the cache; no key is read and no request is sent");
        host.Out.WriteLine(
            $"written to     {Path.Combine(output, ExperimentArtifacts.HeadlineFile)} and one metrics file per scored row");

        return ExitSuccess;
    }

    private static void WriteTally(TextWriter output, ArmMetrics metrics)
    {
        var tally = metrics.Claimed;

        output.WriteLine($"document       {metrics.DocumentId}");
        output.WriteLine($"arm            {ExperimentArtifacts.Code(metrics.Arm)} {ExperimentArtifacts.Name(metrics.Arm)}");
        output.WriteLine($"model          {metrics.Model} ({metrics.Channel})");
        output.WriteLine($"claims         {tally.Claims}");
        output.WriteLine($"found once     {tally.FoundOnce}");
        output.WriteLine($"found more     {tally.FoundMoreThanOnce} than once");
        output.WriteLine($"not found      {tally.NotFound}");
        output.WriteLine($"no quote       {tally.WithoutQuote}");
        output.WriteLine(
            $"not located    {tally.NotLocated} of {tally.Claims}"
            + (tally.NotLocatedShare is null ? string.Empty : $", {ExperimentArtifacts.Percent(tally.NotLocatedShare)}"));
    }

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

        var arm = values.GetValueOrDefault("--arm", PipelineArm);

        if (arm != PipelineArm && (command != "run" || arm != BaselineRun.Arm))
        {
            host.Error.WriteLine($"{command} does not take --arm {arm}. run takes --arm {PipelineArm} (the default) or --arm {BaselineRun.Arm}.");
            return ExitUsage;
        }

        var model = values.GetValueOrDefault("--model", LlmClientFactory.DefaultModel);
        var cacheDirectory = values.GetValueOrDefault("--cache", "cache");
        var offline = flags.Contains(OfflineFlag)
            || CachingLlmClient.IsOffline(host.Environment(CachingLlmClient.OfflineVariable));

        using var httpClient = new HttpClient(host.Network, disposeHandler: false)
        {
            Timeout = arm == BaselineRun.Arm ? BaselineCallTimeout : CallTimeout,
        };

        try
        {
            var client = LlmClientFactory.Create(
                httpClient,
                cacheDirectory,
                offline,
                GeminiLlmClient.ApiKeyFrom(host.Environment(GeminiLlmClient.ApiKeyVariable)));

            var invocation = new Invocation(documentPath, client, model, values.GetValueOrDefault("--out"), offline, arm);

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
            if (args[i] == OfflineFlag || args[i] == HeadlineFlag)
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

    private static void WriteBaselineSummary(
        TextWriter output,
        BaselineRunResult result,
        Invocation invocation,
        string outputDirectory)
    {
        var response = result.Response;

        output.WriteLine($"document       {result.DocumentId}");
        output.WriteLine($"arm            {BaselineRun.Arm}: one naive prompt, no schema, no verification in the loop");
        output.WriteLine($"model          {invocation.Model}");
        output.WriteLine($"mode           {(invocation.Offline ? "offline" : "online")}");
        output.WriteLine(
            $"response       {(response.FromCache ? "from cache" : "from provider")}, "
            + $"{response.InputTokens} tokens in, {response.OutputTokens} out");
        output.WriteLine($"finish reason  {response.FinishReason ?? "not recorded"}");
        output.WriteLine();

        if (result.Answer.Tally is { } tally)
        {
            output.WriteLine($"claims         {tally.Claims} parsed from the answer");
            output.WriteLine($"located        {tally.FoundOnce} exactly once");
            output.WriteLine($"ambiguous      {tally.FoundMoreThanOnce} found more than once");
            output.WriteLine($"not found      {tally.NotFound}");
            output.WriteLine($"no quote       {tally.WithoutQuote}");
        }
        else
        {
            output.WriteLine($"not scored     {result.Answer.Failure}");
        }

        output.WriteLine();
        output.WriteLine($"written to     {outputDirectory}");
    }

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage: spectrace <command> [options]");
        output.WriteLine();
        output.WriteLine("Commands:");
        output.WriteLine("  run     --document <path> [--offline] [--model <id>] [--cache <dir>] [--out <dir>]");
        output.WriteLine("          extract, verify, generate test cases, assemble the matrix and decision queue");
        output.WriteLine("  run     --document <path> --arm baseline [--offline] [--model <id>] [--cache <dir>] [--out <dir>]");
        output.WriteLine("          one naive prompt for requirements, quotes and test cases, scored by the same verifier");
        output.WriteLine("  extract --document <path> [--offline] [--model <id>] [--cache <dir>] [--out <dir>]");
        output.WriteLine("          ask the model for requirements and keep only those located in the source");
        output.WriteLine();
        output.WriteLine("  score   --claims <file> --document <path> [--out <dir>]");
        output.WriteLine("          score an externally produced answer, such as a chat transcript, by the same parser and verifier");
        output.WriteLine("  score   --headline --documents <path,path> [--transcripts <dir>] [--cache <dir>] [--out <dir>]");
        output.WriteLine("          replay every arm offline and write the metrics files and experiments/headline.md");
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
        bool Offline,
        string Arm);
}
