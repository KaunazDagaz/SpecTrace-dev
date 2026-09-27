using System.Globalization;
using SpecTrace.Core;
using SpecTrace.Llm;

namespace SpecTrace.Pipeline;

public static class Experiment
{
    public const string ApiChannel = "Gemini API";

    public static async Task<ArmMetrics> PipelineAsync(
        string documentPath,
        ILlmClient client,
        string model,
        PromptSet prompts,
        GoldReference? gold,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompts);

        var run = await PipelineRun
            .ExecuteAsync(documentPath, client, model, prompts, TimeProvider.System, cancellationToken)
            .ConfigureAwait(false);
        var raw = await File.ReadAllTextAsync(documentPath, cancellationToken).ConfigureAwait(false);
        var document = NormalizedDocument.Create(raw);
        var calls = run.Generations.Select(generation => generation.Response).Prepend(run.Extraction.Response).ToList();

        return new ArmMetrics(
            run.RunId,
            run.DocumentId,
            Arm.Pipeline,
            model,
            ApiChannel,
            run.Extraction.Response.FinishReason,
            ClaimTally.Of(run.Extraction.Candidates.Select(candidate => ClaimTally.OutcomeOf(document.Resolve(candidate.Quote)))),
            ClaimTally.Of(run.Register.Select(requirement => ClaimTally.OutcomeOf(document.Resolve(requirement.Text)))),
            CallCost.Of(run.Extraction.Response, calls),
            Capture: null,
            For(gold, run.DocumentId) is { } scoredAgainst ? QualityScoring.ForPipeline(run, raw, scoredAgainst) : null);
    }

    public static async Task<ArmMetrics> BaselineAsync(
        string documentPath,
        ILlmClient client,
        string model,
        PromptFile prompt,
        GoldReference? gold,
        CancellationToken cancellationToken)
    {
        var run = await BaselineRun
            .ExecuteAsync(documentPath, client, model, prompt, TimeProvider.System, cancellationToken)
            .ConfigureAwait(false);

        var tally = run.Answer.Tally ?? throw new UnparseableAnswerException(
            $"The baseline answer {run.RunId} cannot be scored: {run.Answer.Failure}");

        return new ArmMetrics(
            run.RunId,
            run.DocumentId,
            Arm.Baseline,
            model,
            ApiChannel,
            run.Response.FinishReason,
            tally,
            Delivered: null,
            CallCost.Of(run.Response, [run.Response]),
            Capture: null,
            For(gold, run.DocumentId) is { } scoredAgainst
                ? QualityScoring.ForClaims(
                    run.Answer.Claims,
                    await File.ReadAllTextAsync(documentPath, cancellationToken).ConfigureAwait(false),
                    scoredAgainst)
                : null);
    }

    public static async Task<IReadOnlyList<HeadlineRow>> MeasureAsync(
        IReadOnlyList<string> documentPaths,
        string transcriptDirectory,
        string metricsDirectory,
        ILlmClient client,
        string model,
        PromptSet prompts,
        IReadOnlyList<GoldReference> golds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documentPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(transcriptDirectory);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(golds);

        var rows = new List<HeadlineRow>();

        foreach (var documentPath in documentPaths)
        {
            var documentId = ExtractionRun.DocumentIdFor(documentPath);
            var gold = golds.FirstOrDefault(candidate => candidate.DocumentId == documentId);

            rows.Add(await ChatRowAsync(documentPath, transcriptDirectory, metricsDirectory, prompts.Baseline, gold, cancellationToken)
                .ConfigureAwait(false));
            rows.Add(HeadlineRow.Scored(await BaselineAsync(documentPath, client, model, prompts.Baseline, gold, cancellationToken)
                .ConfigureAwait(false)));
            rows.Add(HeadlineRow.Scored(await PipelineAsync(documentPath, client, model, prompts, gold, cancellationToken)
                .ConfigureAwait(false)));
        }

        return rows;
    }

    public static async Task<ChatScore> ChatAsync(
        string transcriptPath,
        string documentPath,
        PromptFile prompt,
        string metricsDirectory,
        GoldReference? gold,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcriptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricsDirectory);

        var content = await File.ReadAllTextAsync(transcriptPath, cancellationToken).ConfigureAwait(false);
        var transcript = ChatTranscript.Parse(content, Path.GetFileName(documentPath), prompt);
        var raw = await File.ReadAllTextAsync(documentPath, cancellationToken).ConfigureAwait(false);
        var answer = ScoredAnswer.Of(transcript.Answer, NormalizedDocument.Create(raw));
        var documentId = ExtractionRun.DocumentIdFor(documentPath);

        var tally = answer.Tally ?? throw new UnparseableAnswerException(
            $"The chat transcript {transcriptPath} cannot be scored: {answer.Failure}");

        var metrics = new ArmMetrics(
            ChatRunIdFor(documentId, transcript.CapturedAt),
            documentId,
            Arm.Chat,
            transcript.Model,
            transcript.Interface,
            FinishReason: null,
            tally,
            Delivered: null,
            Cost: null,
            new ChatCapture(
                Path.GetRelativePath(metricsDirectory, transcriptPath).Replace('\\', '/'),
                transcript.CapturedAt,
                transcript.Interface,
                transcript.Mode,
                transcript.Input,
                transcript.ShareLink),
            For(gold, documentId) is { } scoredAgainst ? QualityScoring.ForClaims(answer.Claims, raw, scoredAgainst) : null);

        return new ChatScore(metrics, answer.Claims);
    }

    public static string ChatRunIdFor(string documentId, DateTimeOffset capturedAt) =>
        $"{documentId}-chat-{capturedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    private static async Task<HeadlineRow> ChatRowAsync(
        string documentPath,
        string transcriptDirectory,
        string metricsDirectory,
        PromptFile prompt,
        GoldReference? gold,
        CancellationToken cancellationToken)
    {
        var documentId = ExtractionRun.DocumentIdFor(documentPath);
        var transcriptPath = ChatTranscript.PathFor(transcriptDirectory, documentId);
        var shown = Path.GetRelativePath(metricsDirectory, transcriptPath).Replace('\\', '/');

        if (!File.Exists(transcriptPath))
        {
            return HeadlineRow.Unscored(documentId, Arm.Chat, $"pending, no transcript at {shown} yet");
        }

        try
        {
            var score = await ChatAsync(transcriptPath, documentPath, prompt, metricsDirectory, gold, cancellationToken)
                .ConfigureAwait(false);

            return HeadlineRow.Scored(score.Metrics);
        }
        catch (InvalidTranscriptException exception)
        {
            return HeadlineRow.Unscored(
                documentId,
                Arm.Chat,
                $"pending, {shown} is incomplete: {string.Join("; ", exception.Problems)}");
        }
        catch (UnparseableAnswerException exception)
        {
            return HeadlineRow.Unscored(documentId, Arm.Chat, exception.Message);
        }
    }

    private static GoldReference? For(GoldReference? gold, string documentId) =>
        gold is not null && gold.DocumentId == documentId ? gold : null;
}

public sealed record ChatScore(ArmMetrics Metrics, IReadOnlyList<ScoredClaim> Claims);
