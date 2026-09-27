using System.Globalization;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public sealed record ScoredPrediction(
    string Label,
    string? Quote,
    ClaimOutcome Outcome,
    IReadOnlyList<string> Sections,
    Prediction Prediction);

public sealed record QualityAtThreshold(MatchResult Match, QualityScore Score)
{
    public int MinimumOverlapPercent => Match.MinimumOverlapPercent;
}

public sealed record QualityView(
    IReadOnlyList<ScoredPrediction> Predictions,
    IReadOnlyList<QualityAtThreshold> Thresholds,
    IReadOnlyList<DocumentPart> Thirds,
    VerificationCost Cost,
    IReadOnlyList<ApproximatePlace> Places)
{
    public QualityAtThreshold Primary =>
        Thresholds.Single(threshold => threshold.MinimumOverlapPercent == ArmQuality.MinimumOverlapPercent);

    public int ClaimsWithoutQuote => Predictions.Count(prediction => prediction.Outcome == ClaimOutcome.WithoutQuote);
}

public sealed record ApproximatePlace(IReadOnlyList<string> Sections, string Text);

public sealed record HeldBack(int Gold, string Claim, string Reason)
{
    public const string RegisterEntryMatchedElsewhere = "register_entry_matched_another_gold_requirement";

    public const string NotInRegister = "not_in_register";
}

public sealed record GoldPosition(int FirstLine, int LastLine);

public sealed record ArmQuality(
    GoldReference Gold,
    int DocumentLines,
    IReadOnlyList<GoldPosition> GoldPositions,
    QualityView Claimed,
    QualityView? Delivered,
    IReadOnlyList<HeldBack> HeldBack)
{
    public const int MinimumOverlapPercent = SpanMatching.DefaultMinimumOverlapPercent;

    public const int SimilarityThresholdPercent = 90;

    public static IReadOnlyList<int> OverlapPercents { get; } = [30, 50, 70];
}

public static class QualityScoring
{
    public const string NotLocatedSection = "not located";

    public static ArmQuality ForPipeline(PipelineRunResult run, string raw, GoldReference gold)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(gold);

        var document = NormalizedDocument.Create(raw);
        var sections = SectionIndex.Build(raw);

        var claimed = View(
            [.. run.Extraction.Candidates.Select((candidate, index) =>
                Claim(Ordinal(index + 1), candidate.Quote, candidate.Modality, document, sections))],
            document,
            sections,
            raw,
            gold);

        var delivered = View(
            [.. run.Register.Select(requirement => new ScoredPrediction(
                requirement.Id,
                requirement.Text,
                ClaimOutcome.FoundOnce,
                [requirement.Section],
                new Prediction([requirement.Span], requirement.Modality)))],
            document,
            sections,
            raw,
            gold);

        return Quality(gold, raw, claimed, delivered, HeldBackFrom(run, claimed, delivered));
    }

    public static ArmQuality ForClaims(IReadOnlyList<ScoredClaim> claims, string raw, GoldReference gold)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(gold);

        var document = NormalizedDocument.Create(raw);
        var sections = SectionIndex.Build(raw);

        var claimed = View(
            [.. claims.Select(claim => Claim(Ordinal(claim.Claim.Ordinal), claim.Claim.Quote, modality: null, document, sections))],
            document,
            sections,
            raw,
            gold);

        return Quality(gold, raw, claimed, delivered: null, heldBack: []);
    }

    private static ArmQuality Quality(
        GoldReference gold,
        string raw,
        QualityView claimed,
        QualityView? delivered,
        IReadOnlyList<HeldBack> heldBack) =>
        new(
            gold,
            PositionRecall.LineCount(raw),
            [.. gold.Requirements.Select(requirement => new GoldPosition(
                PositionRecall.LineOf(raw, requirement.Span.Start),
                PositionRecall.LineOf(raw, requirement.Span.End - 1)))],
            claimed,
            delivered,
            heldBack);

    private static ScoredPrediction Claim(
        string label,
        string? quote,
        Modality? modality,
        NormalizedDocument document,
        SectionIndex sections)
    {
        IReadOnlyList<TextSpan> spans = quote is null ? [] : document.Occurrences(quote);

        return new ScoredPrediction(
            label,
            quote,
            ClaimTally.OutcomeOf(quote is null ? null : document.Resolve(quote)),
            spans.Count == 0 ? [NotLocatedSection] : [.. spans.Select(span => sections.SectionFor(span)).Distinct()],
            new Prediction(spans, modality));
    }

    private static QualityView View(
        IReadOnlyList<ScoredPrediction> predictions,
        NormalizedDocument document,
        SectionIndex sections,
        string raw,
        GoldReference gold)
    {
        var list = predictions.Select(prediction => prediction.Prediction).ToList();

        var thresholds = ArmQuality.OverlapPercents
            .Select(percent =>
            {
                var match = SpanMatching.Match(list, gold.Requirements, percent);

                return new QualityAtThreshold(match, QualityScore.Of(list, gold.Requirements, match));
            })
            .ToList();

        var primary = thresholds.Single(threshold => threshold.MinimumOverlapPercent == ArmQuality.MinimumOverlapPercent);

        var unlocated = predictions
            .Select((prediction, index) => (Prediction: prediction, Index: index))
            .Where(entry => entry.Prediction.Outcome == ClaimOutcome.NotFound)
            .Select(entry => new UnlocatedQuote(entry.Index, ApproximateLocator.Locate(document, entry.Prediction.Quote!)))
            .ToList();

        return new QualityView(
            predictions,
            thresholds,
            PositionRecall.ByThird(raw, gold.Requirements, primary.Match),
            VerificationCost.Analyse(gold.Requirements, primary.Match, unlocated, ArmQuality.SimilarityThresholdPercent),
            [.. unlocated.Select(quote => new ApproximatePlace(
                [.. quote.Location.Spans.Select(span => sections.SectionFor(span)).Distinct()],
                string.Join(" | ", quote.Location.Spans.Select(span => TextNormalizer.Normalize(raw[span.Start..span.End])).Distinct())))]);
    }

    private static List<HeldBack> HeldBackFrom(PipelineRunResult run, QualityView claimed, QualityView delivered)
    {
        var registerQuotes = run.Register
            .Select(requirement => TextNormalizer.Normalize(requirement.Text))
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. claimed.Primary.Match.Matches
                .Where(match => delivered.Primary.Match.ForGold(match.Gold) is null)
                .OrderBy(match => match.Gold)
                .Select(match =>
                {
                    var claim = claimed.Predictions[match.Prediction];
                    var quote = TextNormalizer.Normalize(claim.Quote!);

                    var reason = claim.Outcome == ClaimOutcome.FoundMoreThanOnce
                        ? QueuedDecision.QuoteFoundMoreThanOnce
                        : registerQuotes.Contains(quote)
                            ? HeldBack.RegisterEntryMatchedElsewhere
                            : run.DecisionQueue
                                .FirstOrDefault(decision => decision.RequirementId is null
                                    && TextNormalizer.Normalize(decision.Item.Quote) == quote)?.Reason
                                ?? HeldBack.NotInRegister;

                    return new HeldBack(match.Gold, claim.Label, reason);
                }),
        ];
    }

    private static string Ordinal(int value) => value.ToString(CultureInfo.InvariantCulture);
}
