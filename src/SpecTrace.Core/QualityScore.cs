namespace SpecTrace.Core;

public sealed record QualityScore
{
    private QualityScore(
        int predictions,
        int located,
        int matched,
        int matchedThroughQuoteFoundMoreThanOnce,
        int gold,
        bool modalityStated,
        int? modalityCorrect)
    {
        Predictions = predictions;
        Located = located;
        Matched = matched;
        MatchedThroughQuoteFoundMoreThanOnce = matchedThroughQuoteFoundMoreThanOnce;
        Gold = gold;
        ModalityStated = modalityStated;
        ModalityCorrect = modalityCorrect;
    }

    public int Predictions { get; }

    public int Located { get; }

    public int NotLocated => Predictions - Located;

    public int Matched { get; }

    public int MatchedThroughQuoteFoundMoreThanOnce { get; }

    public int LocatedWithoutGoldMatch => Located - Matched;

    public int Gold { get; }

    public int GoldMissed => Gold - Matched;

    public double? Precision => Predictions == 0 ? null : (double)Matched / Predictions;

    public double? Recall => Gold == 0 ? null : (double)Matched / Gold;

    public double? F1 => Precision is { } precision && Recall is { } recall
        ? precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall)
        : null;

    public bool ModalityStated { get; }

    public int? ModalityCorrect { get; }

    public double? ModalityAccuracy => ModalityCorrect is { } correct && Matched > 0 ? (double)correct / Matched : null;

    public static QualityScore Of(
        IReadOnlyList<Prediction> predictions,
        IReadOnlyList<GoldRequirement> gold,
        MatchResult match)
    {
        ArgumentNullException.ThrowIfNull(predictions);
        ArgumentNullException.ThrowIfNull(gold);
        ArgumentNullException.ThrowIfNull(match);

        if (match.PredictionCount != predictions.Count || match.GoldCount != gold.Count)
        {
            throw new ArgumentException(
                $"The match covers {match.PredictionCount} predictions and {match.GoldCount} gold requirements, "
                + $"but {predictions.Count} predictions and {gold.Count} gold requirements were given.",
                nameof(match));
        }

        var modalityStated = predictions.Count > 0 && predictions.All(prediction => prediction.Modality is not null);

        return new QualityScore(
            predictions.Count,
            predictions.Count(prediction => prediction.Located),
            match.Matches.Count,
            match.Matches.Count(pair => pair.ThroughQuoteFoundMoreThanOnce),
            gold.Count,
            modalityStated,
            modalityStated
                ? match.Matches.Count(pair => predictions[pair.Prediction].Modality == gold[pair.Gold].Modality)
                : null);
    }
}
