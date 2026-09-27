using System.Text.Json.Serialization;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

internal sealed record QualityJson(
    [property: JsonPropertyName("gold_standard")] GoldStandardJson GoldStandard,
    [property: JsonPropertyName("matching")] MatchingJson Matching,
    [property: JsonPropertyName("claimed")] ScoreJson Claimed,
    [property: JsonPropertyName("delivered")] ScoreJson? Delivered,
    [property: JsonPropertyName("by_min_overlap")] IReadOnlyList<ThresholdJson> ByMinOverlap,
    [property: JsonPropertyName("recall_by_third")] RecallByThirdJson RecallByThird,
    [property: JsonPropertyName("cost_of_verification")] CostOfVerificationJson CostOfVerification)
{
    public static QualityJson From(ArmQuality quality)
    {
        var gold = quality.Gold;

        return new QualityJson(
            new GoldStandardJson(
                gold.FileName,
                gold.Sha256,
                gold.DocumentId,
                gold.Standard.RulesCommit,
                gold.Standard.Annotator,
                gold.Standard.AnnotatedAt,
                gold.Requirements.Count),
            new MatchingJson(
                ArmQuality.MinimumOverlapPercent,
                QualityReport.GreedyRule,
                QualityReport.CountingRule),
            ScoreJson.From(quality.Claimed.Primary.Score),
            quality.Delivered is null ? null : ScoreJson.From(quality.Delivered.Primary.Score),
            [.. ArmQuality.OverlapPercents.Select(percent => new ThresholdJson(
                percent,
                ShortScoreJson.From(At(quality.Claimed, percent)),
                quality.Delivered is null ? null : ShortScoreJson.From(At(quality.Delivered, percent))))],
            new RecallByThirdJson(
                quality.DocumentLines,
                [.. quality.Claimed.Thirds.Select((part, index) => new PartJson(
                    part.Number,
                    part.FirstLine,
                    part.LastLine,
                    part.Gold,
                    part.Matched,
                    Rounded(part.Recall),
                    quality.Delivered?.Thirds[index].Matched,
                    quality.Delivered is null ? null : Rounded(quality.Delivered.Thirds[index].Recall)))]),
            new CostOfVerificationJson(
                QualityReport.SimilarityRule,
                ArmQuality.SimilarityThresholdPercent / 100.0,
                quality.Claimed.Cost.Unlocated.Count,
                quality.Claimed.ClaimsWithoutQuote,
                quality.Claimed.Cost.AtOrAboveThreshold,
                quality.Claimed.Cost.GoldReachedButLost.Count,
                [.. quality.Claimed.Cost.GoldReachedButLost.Select(index => GoldReference.Label(gold.Requirements[index]))],
                quality.Delivered is null ? null : quality.HeldBack.Count,
                quality.Delivered is null ? null : [.. quality.HeldBack.Select(held => GoldReference.Label(gold.Requirements[held.Gold]))]));
    }

    internal static double? Rounded(double? share) => share is { } value ? Math.Round(value, 4) : null;

    private static QualityScore At(QualityView view, int percent) =>
        view.Thresholds.Single(threshold => threshold.MinimumOverlapPercent == percent).Score;
}

internal sealed record GoldStandardJson(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("document_id")] string DocumentId,
    [property: JsonPropertyName("annotation_rules_commit")] string AnnotationRulesCommit,
    [property: JsonPropertyName("annotator")] string Annotator,
    [property: JsonPropertyName("annotated_at")] string AnnotatedAt,
    [property: JsonPropertyName("requirements")] int Requirements);

internal sealed record MatchingJson(
    [property: JsonPropertyName("min_overlap_percent_of_shorter_span")] int MinimumOverlapPercent,
    [property: JsonPropertyName("greedy")] string Greedy,
    [property: JsonPropertyName("counted")] string Counted);

internal sealed record ScoreJson(
    [property: JsonPropertyName("claims")] int Claims,
    [property: JsonPropertyName("located")] int Located,
    [property: JsonPropertyName("not_located")] int NotLocated,
    [property: JsonPropertyName("matched")] int Matched,
    [property: JsonPropertyName("matched_through_a_quote_found_more_than_once")] int MatchedThroughQuoteFoundMoreThanOnce,
    [property: JsonPropertyName("located_without_gold_match")] int LocatedWithoutGoldMatch,
    [property: JsonPropertyName("gold_missed")] int GoldMissed,
    [property: JsonPropertyName("precision")] double? Precision,
    [property: JsonPropertyName("recall")] double? Recall,
    [property: JsonPropertyName("f1")] double? F1,
    [property: JsonPropertyName("modality_correct")] int? ModalityCorrect,
    [property: JsonPropertyName("modality_accuracy")] double? ModalityAccuracy,
    [property: JsonPropertyName("modality_not_applicable")] string? ModalityNotApplicable)
{
    public static ScoreJson From(QualityScore score) => new(
        score.Predictions,
        score.Located,
        score.NotLocated,
        score.Matched,
        score.MatchedThroughQuoteFoundMoreThanOnce,
        score.LocatedWithoutGoldMatch,
        score.GoldMissed,
        QualityJson.Rounded(score.Precision),
        QualityJson.Rounded(score.Recall),
        QualityJson.Rounded(score.F1),
        score.ModalityCorrect,
        QualityJson.Rounded(score.ModalityAccuracy),
        score.ModalityStated ? null : QualityReport.ModalityNotStated);
}

internal sealed record ShortScoreJson(
    [property: JsonPropertyName("matched")] int Matched,
    [property: JsonPropertyName("precision")] double? Precision,
    [property: JsonPropertyName("recall")] double? Recall,
    [property: JsonPropertyName("f1")] double? F1)
{
    public static ShortScoreJson From(QualityScore score) => new(
        score.Matched,
        QualityJson.Rounded(score.Precision),
        QualityJson.Rounded(score.Recall),
        QualityJson.Rounded(score.F1));
}

internal sealed record ThresholdJson(
    [property: JsonPropertyName("min_overlap_percent")] int MinimumOverlapPercent,
    [property: JsonPropertyName("claimed")] ShortScoreJson Claimed,
    [property: JsonPropertyName("delivered")] ShortScoreJson? Delivered);

internal sealed record RecallByThirdJson(
    [property: JsonPropertyName("document_lines")] int DocumentLines,
    [property: JsonPropertyName("parts")] IReadOnlyList<PartJson> Parts);

internal sealed record PartJson(
    [property: JsonPropertyName("part")] int Part,
    [property: JsonPropertyName("first_line")] int FirstLine,
    [property: JsonPropertyName("last_line")] int LastLine,
    [property: JsonPropertyName("gold")] int Gold,
    [property: JsonPropertyName("matched_claimed")] int MatchedClaimed,
    [property: JsonPropertyName("recall_claimed")] double? RecallClaimed,
    [property: JsonPropertyName("matched_delivered")] int? MatchedDelivered,
    [property: JsonPropertyName("recall_delivered")] double? RecallDelivered);

internal sealed record CostOfVerificationJson(
    [property: JsonPropertyName("similarity")] string Similarity,
    [property: JsonPropertyName("similarity_threshold")] double SimilarityThreshold,
    [property: JsonPropertyName("quotes_not_located")] int QuotesNotLocated,
    [property: JsonPropertyName("claims_without_quote")] int ClaimsWithoutQuote,
    [property: JsonPropertyName("quotes_at_or_above_threshold")] int QuotesAtOrAboveThreshold,
    [property: JsonPropertyName("gold_reached_but_lost")] int GoldReachedButLost,
    [property: JsonPropertyName("gold_reached_but_lost_labels")] IReadOnlyList<string> GoldReachedButLostLabels,
    [property: JsonPropertyName("gold_held_back_from_register")] int? GoldHeldBackFromRegister,
    [property: JsonPropertyName("gold_held_back_labels")] IReadOnlyList<string>? GoldHeldBackLabels);
