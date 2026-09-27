using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public static class ExperimentArtifacts
{
    public const string HeadlineFile = "headline.md";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly UTF8Encoding Utf8WithoutMark = new(encoderShouldEmitUTF8Identifier: false);

    public static string Code(Arm arm) => arm switch
    {
        Arm.Chat => "A0",
        Arm.Baseline => "A",
        Arm.Pipeline => "B",
        _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, null),
    };

    public static string Name(Arm arm) => arm switch
    {
        Arm.Chat => "chat",
        Arm.Baseline => "baseline",
        Arm.Pipeline => "pipeline",
        _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, null),
    };

    public static async Task<string> WriteMetricsAsync(ArmMetrics metrics, string directory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, metrics.FileName);

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(MetricsJson.From(metrics), Options) + "\n",
            Utf8WithoutMark,
            cancellationToken).ConfigureAwait(false);

        return path;
    }

    public static async Task WriteHeadlineAsync(
        IReadOnlyList<HeadlineRow> rows,
        string command,
        string directory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
            Path.Combine(directory, HeadlineFile),
            RenderHeadline(rows, command),
            Utf8WithoutMark,
            cancellationToken).ConfigureAwait(false);
    }

    public static string RenderHeadline(IReadOnlyList<HeadlineRow> rows, string command)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var text = new StringBuilder();

        text.Append("# Headline: claimed requirements whose quote cannot be located in the document\n\n");
        text.Append("This file is generated. The command below rebuilds it and every metrics file it names, offline, from\n");
        text.Append("`cache/` and the transcripts in `experiments/a0/`, with no key and no network. CI runs the same command\n");
        text.Append("and fails if the result differs from what is committed. Do not edit it by hand.\n\n");
        text.Append("```\n").Append(command).Append("\n```\n\n");
        text.Append("| Document | Arm | Model | Claims | Not located | Not found | No quote | Found once | Found more than once | Calls | Tokens in / out | Finish reason | Metrics file |\n");
        text.Append("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|\n");

        foreach (var row in rows)
        {
            if (row.Metrics is not { } metrics)
            {
                text.Append($"| {row.DocumentId} | {Code(row.Arm)} {Name(row.Arm)} | — | not scored: {row.NotScored} | | | | | | | | | |\n");
                continue;
            }

            var model = metrics.Arm == Arm.Chat
                ? $"{metrics.Model} ({metrics.Channel})"
                : $"{metrics.Model} ({metrics.Channel}, temperature 0)";
            var calls = metrics.Cost is { } cost ? cost.Calls.ToString(CultureInfo.InvariantCulture) : "—";
            var tokens = metrics.Cost is { } spent ? $"{Thousands(spent.InputTokens)} / {Thousands(spent.OutputTokens)}" : "—";
            var finish = metrics.Arm == Arm.Chat ? "—" : metrics.FinishReason ?? "not recorded";

            text.Append(TallyRow(metrics.DocumentId, ArmLabel(metrics, delivered: false), model, metrics.Claimed, calls, tokens, finish, metrics.FileName));

            if (metrics.Delivered is { } register)
            {
                text.Append(TallyRow(metrics.DocumentId, ArmLabel(metrics, delivered: true), model, register, "—", "—", "—", metrics.FileName));
            }
        }

        text.Append("\n## How to read the table\n\n");
        text.Append("- **Claims** are the items an arm presented as requirements. For A0 and A they are the items the one\n");
        text.Append("  deterministic parser (`ClaimParser`) split the answer into: one claim per quote, and one claim for an\n");
        text.Append("  item with no recognisable quote. For B they are the entries the extraction call returned.\n");
        text.Append("- **Not located** is the headline figure: (quotes not found + claims with no quote) ÷ claims.\n");
        text.Append("- **Found once** is the quote-verification rate, as the pipeline computes it: the quote occurs exactly\n");
        text.Append("  once in the document. **Found more than once** means it occurs, but at several places, so no single\n");
        text.Append("  span can be claimed for it. Not located, found once and found more than once add up to 100%.\n");
        text.Append("- Every arm is scored by the pipeline's own matching: an exact substring match after whitespace is\n");
        text.Append("  collapsed, with no fuzzy matching. The parser never reads the document and never alters a quote; it\n");
        text.Append("  removes only the quotation marks or markup around it.\n");
        text.Append("- **A and B are the controlled comparison**: same model, temperature 0, same document, same verifier.\n");
        text.Append("  A is one naive prompt (`src/SpecTrace.Pipeline/Prompts/baseline.user.md`) with no system prompt and no\n");
        text.Append("  response schema; B is the full pipeline.\n");
        text.Append("- **B, delivered register: zero by design, not a finding.** The pipeline drops every quote it cannot\n");
        text.Append("  locate before anything reaches the register (P3), so this share cannot be anything but zero, and the\n");
        text.Append("  register also holds no quote found more than once: those go to the human decision queue. B's measured\n");
        text.Append("  figure is the row for the model's raw claims.\n");
        text.Append("- **A0 is illustrative and not reproducible.** It was captured by hand from a public chat interface;\n");
        text.Append("  the transcript's front matter records when, which model name the interface showed, and a share\n");
        text.Append("  link. The interface's model version, system prompt, sampling settings and any tools it uses are\n");
        text.Append("  neither disclosed nor under our control, and a chat cannot be replayed from a cache, so asking again\n");
        text.Append("  may give a different answer. Only the scoring of the archived answer is reproducible. It is reported\n");
        text.Append("  for scale, not as a controlled result, and its result on each document is reported whichever way it\n");
        text.Append("  came out.\n");
        text.Append("- **Calls and tokens** are the provider's own counts (`promptTokenCount`, `candidatesTokenCount`),\n");
        text.Append("  recorded in `cache/` when each call was made live. The chat interface reports none. Every row is\n");
        text.Append("  replayed from the cache, so the cache hit rate in each metrics file is 100% by construction.\n");
        text.Append("- **Finish reason** is the provider's reason for ending the whole-document answer. A was allowed the\n");
        text.Append("  model's full output limit, so our own setting never cut it short; anything but `STOP` would mean the\n");
        text.Append("  provider did. B's extraction is requested with a response schema, and a structured answer that stops\n");
        text.Append("  for any other reason is refused and never cached; entries cached before finish reasons were kept\n");
        text.Append("  show `not recorded`.\n");

        return text.ToString();
    }

    private static string ArmLabel(ArmMetrics metrics, bool delivered) => metrics.Arm switch
    {
        Arm.Chat => "A0 chat — illustrative, not reproducible",
        Arm.Baseline => "A baseline",
        Arm.Pipeline when delivered => "B pipeline, delivered register — zero by design",
        Arm.Pipeline => "B pipeline, model's raw claims",
        _ => throw new ArgumentOutOfRangeException(nameof(metrics), metrics.Arm, null),
    };

    private static string TallyRow(
        string documentId,
        string arm,
        string model,
        ClaimTally tally,
        string calls,
        string tokens,
        string finish,
        string file) =>
        $"| {documentId} | {arm} | {model} | {tally.Claims} | {Percent(tally.NotLocatedShare)} ({tally.NotLocated}) "
        + $"| {tally.NotFound} | {tally.WithoutQuote} | {tally.FoundOnce} ({Percent(tally.VerificationRate)}) "
        + $"| {tally.FoundMoreThanOnce} ({Percent(tally.FoundMoreThanOnceShare)}) | {calls} | {tokens} | {finish} "
        + $"| [{file}]({file}) |\n";

    public static string Percent(double? share) =>
        share is { } value ? (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—";

    private static string Thousands(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static double? Rounded(double? share) => share is { } value ? Math.Round(value, 4) : null;

    private sealed record MetricsJson(
        [property: JsonPropertyName("run_id")] string RunId,
        [property: JsonPropertyName("document_id")] string DocumentId,
        [property: JsonPropertyName("arm")] string Arm,
        [property: JsonPropertyName("arm_name")] string ArmName,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("channel")] string Channel,
        [property: JsonPropertyName("reproducible")] bool Reproducible,
        [property: JsonPropertyName("finish_reason")] string? FinishReason,
        [property: JsonPropertyName("claimed")] TallyJson Claimed,
        [property: JsonPropertyName("delivered")] TallyJson? Delivered,
        [property: JsonPropertyName("cost")] CostJson? Cost,
        [property: JsonPropertyName("capture")] CaptureJson? Capture)
    {
        public static MetricsJson From(ArmMetrics metrics) => new(
            metrics.RunId,
            metrics.DocumentId,
            Code(metrics.Arm),
            Name(metrics.Arm),
            metrics.Model,
            metrics.Channel,
            metrics.Reproducible,
            metrics.FinishReason,
            TallyJson.From(metrics.Claimed),
            metrics.Delivered is null ? null : TallyJson.From(metrics.Delivered),
            metrics.Cost is null ? null : CostJson.From(metrics.Cost),
            metrics.Capture is null ? null : CaptureJson.From(metrics.Capture));
    }

    private sealed record TallyJson(
        [property: JsonPropertyName("claims")] int Claims,
        [property: JsonPropertyName("quotes_found")] int QuotesFound,
        [property: JsonPropertyName("quotes_found_once")] int QuotesFoundOnce,
        [property: JsonPropertyName("quotes_found_more_than_once")] int QuotesFoundMoreThanOnce,
        [property: JsonPropertyName("quotes_not_found")] int QuotesNotFound,
        [property: JsonPropertyName("claims_without_quote")] int ClaimsWithoutQuote,
        [property: JsonPropertyName("not_located_share")] double? NotLocatedShare,
        [property: JsonPropertyName("verification_rate")] double? VerificationRate,
        [property: JsonPropertyName("found_more_than_once_share")] double? FoundMoreThanOnceShare)
    {
        public static TallyJson From(ClaimTally tally) => new(
            tally.Claims,
            tally.QuotesFound,
            tally.FoundOnce,
            tally.FoundMoreThanOnce,
            tally.NotFound,
            tally.WithoutQuote,
            Rounded(tally.NotLocatedShare),
            Rounded(tally.VerificationRate),
            Rounded(tally.FoundMoreThanOnceShare));
    }

    private sealed record CostJson(
        [property: JsonPropertyName("calls")] int Calls,
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens,
        [property: JsonPropertyName("whole_document_call_input_tokens")] int WholeDocumentInputTokens,
        [property: JsonPropertyName("whole_document_call_output_tokens")] int WholeDocumentOutputTokens,
        [property: JsonPropertyName("cache_hits")] int CacheHits,
        [property: JsonPropertyName("cache_hit_rate")] double CacheHitRate)
    {
        public static CostJson From(CallCost cost) => new(
            cost.Calls,
            cost.InputTokens,
            cost.OutputTokens,
            cost.WholeDocumentInputTokens,
            cost.WholeDocumentOutputTokens,
            cost.CacheHits,
            Math.Round(cost.CacheHitRate, 4));
    }

    private sealed record CaptureJson(
        [property: JsonPropertyName("transcript")] string Transcript,
        [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
        [property: JsonPropertyName("interface")] string Interface,
        [property: JsonPropertyName("mode")] string Mode,
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("share_link")] string ShareLink)
    {
        public static CaptureJson From(ChatCapture capture) => new(
            capture.Transcript,
            capture.CapturedAt,
            capture.Interface,
            capture.Mode,
            capture.Input,
            capture.ShareLink);
    }
}

public sealed record HeadlineRow
{
    private HeadlineRow(string documentId, Arm arm, ArmMetrics? metrics, string? notScored)
    {
        DocumentId = documentId;
        Arm = arm;
        Metrics = metrics;
        NotScored = notScored;
    }

    public string DocumentId { get; }

    public Arm Arm { get; }

    public ArmMetrics? Metrics { get; }

    public string? NotScored { get; }

    public static HeadlineRow Scored(ArmMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return new HeadlineRow(metrics.DocumentId, metrics.Arm, metrics, notScored: null);
    }

    public static HeadlineRow Unscored(string documentId, Arm arm, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new HeadlineRow(documentId, arm, metrics: null, reason);
    }
}
