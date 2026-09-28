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

    public const string GoldOption = "--gold";

    public const string CorpusDirectory = "corpus";

    private static readonly TimeSpan CallTimeout = PipelineLaunch.CallTimeout;

    private static readonly TimeSpan BaselineCallTimeout = TimeSpan.FromMinutes(20);

    private static readonly System.Text.UTF8Encoding Utf8WithoutMark = new(encoderShouldEmitUTF8Identifier: false);

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

            case "export":
                return await ExportAsync(args[1..], host, cancellationToken).ConfigureAwait(false);

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

            var (result, outputDirectory) = await PipelineLaunch
                .RunAsync(invocation.DocumentPath, invocation.Client, invocation.Model, host.Prompts, TimeProvider.System, invocation.OutputDirectory, token)
                .ConfigureAwait(false);

            WriteRunSummary(host.Out, result, invocation, outputDirectory);
        }, cancellationToken);

    private static async Task<int> ScoreAsync(string[] args, CliHost host, CancellationToken cancellationToken)
    {
        if (!TryParse(args, host.Error, out var values, out var flags))
        {
            return ExitUsage;
        }

        if (values.TryGetValue("--worksheet", out var worksheet))
        {
            return await WorksheetAsync(worksheet, values, host, cancellationToken).ConfigureAwait(false);
        }

        if (values.TryGetValue("--reviews", out var reviews))
        {
            return await ReviewsAsync(reviews, values, host, cancellationToken).ConfigureAwait(false);
        }

        if (values.TryGetValue(GoldOption, out var gold) && !values.ContainsKey("--run") && !values.ContainsKey("--claims") && !flags.Contains(HeadlineFlag))
        {
            return await GoldAsync(gold, values, host, cancellationToken).ConfigureAwait(false);
        }

        var output = values.GetValueOrDefault("--out", ExperimentsDirectory);

        try
        {
            if (flags.Contains(HeadlineFlag))
            {
                return await HeadlineAsync(values, output, host, cancellationToken).ConfigureAwait(false);
            }

            if (values.TryGetValue("--run", out var runId))
            {
                return await RunScoreAsync(runId, values, output, host, cancellationToken).ConfigureAwait(false);
            }

            if (values.TryGetValue("--claims", out var claims))
            {
                return await ClaimsAsync(claims, values, output, host, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (InvalidGoldFileException exception)
        {
            host.Error.WriteLine(exception.Message);
            host.Error.WriteLine("The gold file does not load, so nothing was scored.");
            return ExitFailure;
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

        host.Error.WriteLine(
            "score needs --claims <file> --document <path>, --headline --documents <path,path>, or --run <id> --gold <path>.");
        WriteUsage(host.Error);
        return ExitUsage;
    }

    private static async Task<int> ExportAsync(string[] args, CliHost host, CancellationToken cancellationToken)
    {
        if (!TryParse(args, host.Error, out var values, out _))
        {
            return ExitUsage;
        }

        if (!values.TryGetValue("--run", out var key))
        {
            host.Error.WriteLine("export needs --run <key>: reference, or the ID of a run started from the web UI.");
            return ExitUsage;
        }

        string[] formats = values.TryGetValue("--format", out var format)
            ? [format]
            : [MatrixExport.MarkdownFormat, MatrixExport.CsvFormat];

        if (formats.Any(candidate => candidate is not (MatrixExport.MarkdownFormat or MatrixExport.CsvFormat)))
        {
            host.Error.WriteLine($"--format takes {MatrixExport.MarkdownFormat} or {MatrixExport.CsvFormat}.");
            return ExitUsage;
        }

        var workspace = Workspace.Default with
        {
            Corpus = values.GetValueOrDefault("--corpus", Workspace.Default.Corpus),
            Runs = values.GetValueOrDefault("--runs", Workspace.Default.Runs),
            Reference = values.GetValueOrDefault("--reference", Workspace.Default.Reference),
        };

        if (!Workspace.IsRunKey(key))
        {
            host.Error.WriteLine($"'{key}' is not a run key: use reference, or a run ID such as rfc6902-3ff2234db6aa.");
            return ExitUsage;
        }

        LoadedRun run;

        try
        {
            run = await new RunCatalog(workspace, TimeProvider.System)
                .LoadAsync(key, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RunNotAvailableException exception)
        {
            host.Error.WriteLine(exception.Message);
            host.Error.WriteLine("Nothing was exported.");
            return ExitFailure;
        }

        var directory = values.GetValueOrDefault("--out", Path.Combine(workspace.WebRoot, "exports"));

        Directory.CreateDirectory(directory);

        host.Out.WriteLine($"run            {run.RunId} ({key})");
        host.Out.WriteLine($"review log     {run.Log.Count} decisions from {run.LogPath}");

        foreach (var chosen in formats)
        {
            var path = Path.Combine(directory, $"{key}.matrix.{(chosen == MatrixExport.MarkdownFormat ? "md" : "csv")}");

            await File.WriteAllTextAsync(path, MatrixExport.Render(run, chosen), Utf8WithoutMark, cancellationToken)
                .ConfigureAwait(false);

            host.Out.WriteLine($"written to     {path}");
        }

        host.Out.WriteLine();
        host.Out.WriteLine(
            "The reviewed matrix is computed from the run's files and its review log; neither was changed. "
            + MatrixHtml.NoCompletenessClaim);

        return ExitSuccess;
    }

    private static async Task<int> ReviewsAsync(
        string log,
        Dictionary<string, string> values,
        CliHost host,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LoggedDecision> decisions;
        byte[] bytes;

        try
        {
            bytes = await File.ReadAllBytesAsync(log, cancellationToken).ConfigureAwait(false);
            decisions = ReviewLog.Parse(log, bytes);
        }
        catch (Exception exception) when (exception is InvalidReviewLogException
            or FileNotFoundException
            or DirectoryNotFoundException)
        {
            host.Error.WriteLine(exception.Message);
            host.Error.WriteLine("Nothing was summarised.");
            return ExitFailure;
        }

        var outcomes = ReviewOutcomes.Of(decisions);
        var summary = ReviewSummary.Render(Path.GetFileName(log), bytes, outcomes);

        host.Out.Write(summary);

        if (values.TryGetValue("--out", out var directory))
        {
            var name = outcomes.RunIds is [var runId] ? runId : Path.GetFileNameWithoutExtension(log);
            var path = Path.Combine(directory, $"{name}.review-outcomes.md");

            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, summary, Utf8WithoutMark, cancellationToken).ConfigureAwait(false);

            host.Out.WriteLine();
            host.Out.WriteLine($"written to     {path}");
        }

        return ExitSuccess;
    }

    private static async Task<int> WorksheetAsync(
        string worksheet,
        Dictionary<string, string> values,
        CliHost host,
        CancellationToken cancellationToken)
    {
        if (!values.TryGetValue("--document", out var documentPath))
        {
            host.Error.WriteLine("score --worksheet needs --document <path>.");
            return ExitUsage;
        }

        try
        {
            var candidates = await AnnotationWorksheet
                .WriteAsync(documentPath, worksheet, cancellationToken)
                .ConfigureAwait(false);

            host.Out.WriteLine($"document       {documentPath}");
            host.Out.WriteLine($"candidates     {candidates} sentences carry an uppercase BCP 14 keyword");
            host.Out.WriteLine($"written to     {worksheet}");
            host.Out.WriteLine();
            host.Out.WriteLine(
                "Found by a keyword scan of the de-paginated text; no model was called. Every annotator field is empty.");

            return ExitSuccess;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or FileNotFoundException
            or DirectoryNotFoundException)
        {
            host.Error.WriteLine(exception.Message);
            host.Error.WriteLine("No worksheet was written.");
            return ExitFailure;
        }
    }

    private static async Task<int> GoldAsync(
        string gold,
        Dictionary<string, string> values,
        CliHost host,
        CancellationToken cancellationToken)
    {
        if (!values.TryGetValue("--document", out var documentPath))
        {
            host.Error.WriteLine("score --gold needs --document <path>.");
            return ExitUsage;
        }

        GoldCheckResult result;

        try
        {
            result = await GoldFile
                .CheckAsync(gold, documentPath, AnnotationRules.FrozenCommit, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidGoldFileException
            or FileNotFoundException
            or DirectoryNotFoundException)
        {
            host.Error.WriteLine(exception.Message);
            host.Error.WriteLine("The gold file does not load.");
            return ExitFailure;
        }

        host.Out.WriteLine($"gold file      {gold}");
        host.Out.WriteLine($"document       {documentPath}");
        host.Out.WriteLine(
            $"rules          {AnnotationRules.Location}, "
            + (AnnotationRules.FrozenCommit.Length == 0 ? "not frozen yet" : $"frozen at {AnnotationRules.FrozenCommit}"));
        host.Out.WriteLine(
            $"candidates     {result.Kept + result.Dropped + result.Undecided} in the file: "
            + $"{result.Kept} keep, {result.Dropped} drop, {result.Undecided} undecided");
        host.Out.WriteLine($"located        {result.Requirements.Count} quotes found exactly once");

        foreach (var requirement in result.Requirements)
        {
            host.Out.WriteLine(
                $"  {$"{requirement.Candidate}.{requirement.Obligation}",-6} {requirement.Section,-6} "
                + $"{RunArtifacts.Spell(requirement.Modality),-10} {RunArtifacts.Spell(requirement.Testability),-20} "
                + TextNormalizer.Normalize(requirement.Quote));
        }

        if (result.Standard is { } standard)
        {
            host.Out.WriteLine();
            host.Out.WriteLine(
                $"The gold file loads: {standard.Requirements.Count} requirements under the frozen rules, "
                + "each quote found exactly once in the document.");
            return ExitSuccess;
        }

        host.Error.WriteLine($"problems       {result.Problems.Count}");

        foreach (var problem in result.Problems)
        {
            host.Error.WriteLine(
                $"  {(problem.Line > 0 ? $"line {problem.Line}" : "file"),-10} {problem.Message}");
        }

        host.Error.WriteLine();
        host.Error.WriteLine(
            $"The gold file does not load: {result.Problems.Count} problems. One problem rejects the whole file.");

        return ExitFailure;
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

        var gold = values.TryGetValue(GoldOption, out var goldPath)
            ? await GoldFile.LoadReferenceAsync(goldPath, documentPath, AnnotationRules.FrozenCommit, cancellationToken).ConfigureAwait(false)
            : null;
        var score = await Experiment
            .ChatAsync(claims, documentPath, host.Prompts.Baseline, output, gold, cancellationToken)
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
        var goldPaths = values.TryGetValue(GoldOption, out var goldList)
            ? goldList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        var golds = new List<GoldReference>();

        foreach (var goldPath in goldPaths)
        {
            var documentId = await GoldFile.DocumentIdOfAsync(goldPath, cancellationToken).ConfigureAwait(false);
            var documentPath = documents.FirstOrDefault(path => ExtractionRun.DocumentIdFor(path) == documentId);

            if (documentPath is null)
            {
                host.Error.WriteLine($"{goldPath} annotates {documentId}, which is not among --documents {list}.");
                return ExitUsage;
            }

            golds.Add(await GoldFile
                .LoadReferenceAsync(goldPath, documentPath, AnnotationRules.FrozenCommit, cancellationToken)
                .ConfigureAwait(false));
        }

        var command = HeadlineCommandPrefix + list + (goldList is null ? string.Empty : $" {GoldOption} {goldList}");
        var rows = await MeasureOfflineAsync(documents, golds, values, output, host, cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            if (row.Metrics is { } metrics)
            {
                await ExperimentArtifacts.WriteMetricsAsync(metrics, output, cancellationToken).ConfigureAwait(false);
            }
        }

        await ExperimentArtifacts
            .WriteHeadlineAsync(rows, command, output, cancellationToken)
            .ConfigureAwait(false);

        var quality = await ExperimentArtifacts
            .WriteQualityAsync(rows, command, output, cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            host.Out.WriteLine(
                $"{row.DocumentId,-12} {ExperimentArtifacts.Code(row.Arm),-3} {ExperimentArtifacts.Name(row.Arm),-9} "
                + (row.Metrics is { } metrics
                    ? $"{metrics.Claimed.NotLocated} of {metrics.Claimed.Claims} claims not located"
                    : $"not scored: {row.NotScored}"));
        }

        foreach (var view in QualityReport.ScoredRows(rows))
        {
            WriteQuality(host.Out, view);
        }

        host.Out.WriteLine();
        host.Out.WriteLine("mode           offline, replayed from the cache; no key is read and no request is sent");
        host.Out.WriteLine(
            $"written to     {Path.Combine(output, ExperimentArtifacts.HeadlineFile)} and one metrics file per scored row");

        foreach (var file in quality)
        {
            host.Out.WriteLine($"               {file}");
        }

        return ExitSuccess;
    }

    private static async Task<int> RunScoreAsync(
        string runId,
        Dictionary<string, string> values,
        string output,
        CliHost host,
        CancellationToken cancellationToken)
    {
        if (!values.TryGetValue(GoldOption, out var goldPath))
        {
            host.Error.WriteLine("score --run <id> needs --gold <path>.");
            return ExitUsage;
        }

        var documentId = await GoldFile.DocumentIdOfAsync(goldPath, cancellationToken).ConfigureAwait(false);

        if (!runId.StartsWith(documentId + "-", StringComparison.Ordinal))
        {
            host.Error.WriteLine($"Run {runId} is not a run over {documentId}, the document {goldPath} annotates.");
            return ExitFailure;
        }

        var documentPath = values.GetValueOrDefault("--document", Path.Combine(CorpusDirectory, documentId + ".txt"));
        var gold = await GoldFile
            .LoadReferenceAsync(goldPath, documentPath, AnnotationRules.FrozenCommit, cancellationToken)
            .ConfigureAwait(false);
        var rows = await MeasureOfflineAsync([documentPath], [gold], values, output, host, cancellationToken).ConfigureAwait(false);
        var metrics = rows.Select(row => row.Metrics).OfType<ArmMetrics>().FirstOrDefault(scored => scored.RunId == runId);

        if (metrics is null)
        {
            host.Error.WriteLine(
                $"Run {runId} cannot be replayed from the cache and the transcripts with the current prompts, model and corpus. "
                + $"The runs over {documentId} that can: "
                + string.Join(", ", rows.Select(row => row.Metrics?.RunId).OfType<string>())
                + ".");
            return ExitFailure;
        }

        var written = await ExperimentArtifacts.WriteMetricsAsync(metrics, output, cancellationToken).ConfigureAwait(false);

        foreach (var view in QualityReport.ScoredRows([HeadlineRow.Scored(metrics)]))
        {
            WriteQuality(host.Out, view);
        }

        host.Out.WriteLine();
        host.Out.WriteLine("mode           offline, replayed from the cache; no key is read and no request is sent");
        host.Out.WriteLine($"written to     {written}");
        host.Out.WriteLine();
        host.Out.WriteLine(
            "A claim counts only through a quote located in the source; a claim that cannot be located is a false positive.");

        return ExitSuccess;
    }

    private static async Task<IReadOnlyList<HeadlineRow>> MeasureOfflineAsync(
        IReadOnlyList<string> documents,
        IReadOnlyList<GoldReference> golds,
        Dictionary<string, string> values,
        string output,
        CliHost host,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient(host.Network, disposeHandler: false) { Timeout = CallTimeout };

        var client = LlmClientFactory.Create(
            httpClient,
            values.GetValueOrDefault("--cache", "cache"),
            offline: true,
            apiKey: null);

        return await Experiment
            .MeasureAsync(
                documents,
                values.GetValueOrDefault("--transcripts", TranscriptsDirectory),
                output,
                client,
                values.GetValueOrDefault("--model", LlmClientFactory.DefaultModel),
                host.Prompts,
                golds,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void WriteQuality(TextWriter output, QualityReport.Scored view)
    {
        var score = view.View.Primary.Score;

        output.WriteLine(
            $"{view.Metrics.DocumentId,-12} {view.Label,-23} "
            + $"precision {QualityReport.Share(score.Matched, score.Predictions),-16} "
            + $"recall {QualityReport.Share(score.Matched, score.Gold),-16} "
            + $"F1 {ExperimentArtifacts.Percent(score.F1),-7} "
            + $"modality {(score.ModalityStated ? QualityReport.Share(score.ModalityCorrect ?? 0, score.Matched) : "n/a")}");
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
        var offline = PipelineLaunch.IsOffline(host.Environment, flags.Contains(OfflineFlag));

        using var httpClient = new HttpClient(host.Network, disposeHandler: false)
        {
            Timeout = arm == BaselineRun.Arm ? BaselineCallTimeout : CallTimeout,
        };

        try
        {
            var client = PipelineLaunch.ModelClients(httpClient, host.Environment)(cacheDirectory, offline);

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
        output.WriteLine("  score   --claims <file> --document <path> [--gold <path>] [--out <dir>]");
        output.WriteLine("          score an externally produced answer, such as a chat transcript, by the same parser and verifier");
        output.WriteLine("  score   --headline --documents <path,path> [--gold <path>] [--transcripts <dir>] [--cache <dir>] [--out <dir>]");
        output.WriteLine("          replay every arm offline and write the metrics files and experiments/headline.md;");
        output.WriteLine("          with --gold, also score every arm on that document against the gold standard");
        output.WriteLine("  score   --run <id> --gold <path> [--document <path>] [--transcripts <dir>] [--cache <dir>] [--out <dir>]");
        output.WriteLine("          replay one run offline, score it against the gold standard, add the fields to its metrics file");
        output.WriteLine("  score   --worksheet <file> --document <path>");
        output.WriteLine("          write an annotation worksheet: every sentence with an uppercase BCP 14 keyword");
        output.WriteLine("  score   --gold <file> --document <path>");
        output.WriteLine("          check a gold file: every quote found exactly once, every value allowed, rules frozen");
        output.WriteLine("  score   --reviews <log> [--out <dir>]");
        output.WriteLine("          summarise a review log: cases accepted, edited and rejected, by each case's latest decision");
        output.WriteLine();
        output.WriteLine("  export  --run <key> [--format markdown|csv] [--runs <dir>] [--reference <dir>] [--corpus <dir>] [--out <dir>]");
        output.WriteLine("          export the reviewed matrix, computed from the run's files and its review log");
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
