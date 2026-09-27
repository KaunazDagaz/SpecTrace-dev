using System.Globalization;
using System.Text;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public static class QualityReport
{
    public const string ChunkingFile = "chunking-decision.md";

    public const string ErrorAnalysisFile = "error-analysis.md";

    public const string UnmatchedHeading = "## Unmatched claims";

    public const string GreedyRule =
        "each gold requirement matches at most one claim and each claim at most one gold requirement; pairs are taken "
        + "greedily by overlap size, largest first; ties go to the gold requirement earlier in the document, then to "
        + "the earlier claim; a claim whose quote is found more than once may match through any occurrence";

    public const string CountingRule =
        "a claim counts only through a quote located in the source; a claim whose quote cannot be located, or that "
        + "has no quote, is a false positive";

    public const string SimilarityRule =
        "1 - (Levenshtein distance between the quote and the closest stretch of the document) / (quote length), "
        + "both whitespace-collapsed; the closest gold requirement is the one that stretch overlaps by at least 50% "
        + "of the shorter span; analysis only, never counted as verified";

    public const string ModalityNotStated =
        "the arm states no modality: the naive prompt it shares asks for none, and a modality read off the keyword "
        + "inside the quote would be our reading, not the arm's";

    public static string FileFor(string documentId) => $"{documentId}.quality.md";

    public static IReadOnlyList<Scored> ScoredRows(IReadOnlyList<HeadlineRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return
        [
            .. rows
                .Select(row => row.Metrics)
                .OfType<ArmMetrics>()
                .Where(metrics => metrics.Quality is not null)
                .SelectMany(metrics => Views(metrics)),
        ];
    }

    public static string RenderHeadlineSection(IReadOnlyList<HeadlineRow> rows)
    {
        var scored = ScoredRows(rows);

        if (scored.Count == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder();

        text.Append("\n## Extraction quality against the gold standard\n\n");

        foreach (var gold in scored.Select(view => view.Metrics.Quality!.Gold).DistinctBy(gold => gold.DocumentId))
        {
            text.Append(
                $"{gold.DocumentId} is scored against `{gold.FileName}`: {gold.Requirements.Count} requirements annotated by hand "
                + $"under the annotation rules frozen at SpecTrace-docs commit `{Short(gold.Standard.RulesCommit)}`.\n");
        }

        var unscored = rows
            .Select(row => row.DocumentId)
            .Distinct()
            .Where(document => scored.All(view => view.Metrics.DocumentId != document))
            .ToList();

        if (unscored.Count > 0)
        {
            text.Append($"{string.Join(" and ", unscored)} has no gold standard and is not scored here.\n");
        }

        text.Append("\n| Document | Arm | Claims | Matched | Through a quote found more than once | Located, no gold match | Not located | Precision | Recall | F1 | Modality accuracy |\n");
        text.Append("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|\n");

        foreach (var view in scored)
        {
            var score = view.View.Primary.Score;

            text.Append(
                $"| {view.Metrics.DocumentId} | {HeadlineLabel(view)} | {score.Predictions} | {score.Matched} "
                + $"| {score.MatchedThroughQuoteFoundMoreThanOnce} | {score.LocatedWithoutGoldMatch} | {score.NotLocated} "
                + $"| {Share(score.Matched, score.Predictions)} | {Share(score.Matched, score.Gold)} | {Percent(score.F1)} "
                + $"| {Modality(score)} |\n");
        }

        text.Append("\n### The same matching at 30% and 70% overlap\n\n");
        text.Append("| Document | Arm | ");
        text.Append(string.Join(" | ", ArmQuality.OverlapPercents.Select(percent => $"{percent}%: P / R / F1")));
        text.Append(" | Smallest overlap of a matched pair at 50% |\n|---|---|");
        text.Append(string.Concat(ArmQuality.OverlapPercents.Select(_ => "---|")));
        text.Append("---:|\n");

        foreach (var view in scored)
        {
            text.Append($"| {view.Metrics.DocumentId} | {HeadlineLabel(view)} | ");
            text.Append(string.Join(" | ", view.View.Thresholds.Select(threshold =>
                $"{Percent(threshold.Score.Precision)} / {Percent(threshold.Score.Recall)} / {Percent(threshold.Score.F1)}")));
            text.Append($" | {Percent(SmallestOverlap(view))} |\n");
        }

        text.Append("\n### Cost of verification\n\n");
        text.Append($"| Document | Arm | Quotes not located | Of those, similarity ≥ {Threshold()} | Gold reached but lost | Gold held back from the register |\n");
        text.Append("|---|---|---:|---:|---:|---|\n");

        foreach (var view in scored)
        {
            var cost = view.View.Cost;
            var heldBack = view.Delivered
                ? $"{view.Metrics.Quality!.HeldBack.Count}: {Labels(view.Metrics.Quality!, view.Metrics.Quality!.HeldBack.Select(held => held.Gold))}"
                : "—";

            text.Append(
                $"| {view.Metrics.DocumentId} | {HeadlineLabel(view)} | {cost.Unlocated.Count} | {cost.AtOrAboveThreshold} "
                + $"| {cost.GoldReachedButLost.Count} | {heldBack} |\n");
        }

        text.Append("\n### How to read these tables\n\n");
        text.Append("- **A claim counts only through a quote that locates in the source.** A claim whose quote cannot be\n");
        text.Append("  found verbatim, after whitespace is collapsed, is a false positive however close its text comes to a\n");
        text.Append("  requirement. Nothing the approximate matching under *Cost of verification* finds is ever counted as\n");
        text.Append("  matched.\n");
        text.Append("- **Matched**: a located claim's span overlaps a gold requirement's span by at least 50% of the shorter\n");
        text.Append("  of the two. Each gold requirement matches at most one claim and each claim at most one gold\n");
        text.Append("  requirement, greedily by overlap size, largest first; ties go to the gold requirement earlier in the\n");
        text.Append("  document, then to the earlier claim. A claim whose quote is found more than once may match through\n");
        text.Append("  any of its occurrences; those matches are also counted in their own column.\n");
        text.Append("- **Precision** = matched ÷ claims, every claim counted, located or not. **Recall** = matched ÷ gold\n");
        text.Append("  requirements. **F1** is their harmonic mean.\n");
        text.Append("- **Modality accuracy** = matched pairs whose modality equals the gold modality ÷ matched pairs. A0 and A\n");
        text.Append("  show n/a: the naive prompt they share asks for no modality and their answers state none, so a\n");
        text.Append("  modality read off the keyword inside the quote would be our reading, not the arm's.\n");
        text.Append("- **B raw** scores every entry the extraction call returned. **B delivered** scores the register, the\n");
        text.Append("  requirements the pipeline delivers after verification; a quote found more than once, or claimed twice\n");
        text.Append("  with different modalities, goes to the human decision queue instead, and the gold requirements it\n");
        text.Append("  matched in B raw are the ones *held back from the register*.\n");
        text.Append("- **30% and 70%**: 50% is a judgment call, so the same matching is repeated with the other two\n");
        text.Append("  thresholds; nothing else changes. A threshold can only change a pair whose overlap lies between the\n");
        text.Append("  two thresholds compared, so the last column bounds how far the 50% figures could move.\n");
        text.Append("- **Cost of verification**: for every quote an arm gave that cannot be located, the closest stretch of\n");
        text.Append($"  the document by Levenshtein distance; similarity = 1 − distance ÷ quote length, threshold {Threshold()},\n");
        text.Append("  both fixed before the analysis was run. *Gold reached but lost* counts the gold requirements such a\n");
        text.Append("  quote lands on, by the same 50% overlap, that no located claim of the same arm matched: the model\n");
        text.Append("  reached them, and lost them because its quote was not verbatim.\n");

        foreach (var document in scored.Select(view => view.Metrics.DocumentId).Distinct())
        {
            text.Append($"- Every claim, match and missed requirement on {document}: [{FileFor(document)}]({FileFor(document)}).\n");
        }

        text.Append($"- Recall by third of the document and the chunking decision: [{ChunkingFile}]({ChunkingFile}).\n");
        text.Append($"- The error analysis, written by hand from these files and not generated: [{ErrorAnalysisFile}]({ErrorAnalysisFile}).\n");

        return text.ToString();
    }

    public static string RenderDocument(IReadOnlyList<HeadlineRow> rows, string documentId, string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var scored = ScoredRows(rows).Where(view => view.Metrics.DocumentId == documentId).ToList();

        if (scored.Count == 0)
        {
            throw new ArgumentException($"No arm on {documentId} was scored against a gold standard.", nameof(rows));
        }

        var quality = scored[0].Metrics.Quality!;
        var gold = quality.Gold;
        var text = new StringBuilder();

        text.Append($"# Extraction quality on {documentId}: every claim, match and missed requirement\n\n");
        Generated(text, command);
        text.Append(
            $"Gold standard: `{gold.FileName}`, SHA-256 `{gold.Sha256}`, {gold.Requirements.Count} requirements, annotated by "
            + $"{gold.Standard.Annotator} ({gold.Standard.AnnotatedAt}) under the annotation rules frozen at SpecTrace-docs "
            + $"commit `{gold.Standard.RulesCommit}`. The rules count only statements that carry an uppercase BCP 14 keyword, "
            + "so recall is not split into gold requirements with and without a keyword.\n\n");
        text.Append($"Matching: at least {ArmQuality.MinimumOverlapPercent}% overlap of the shorter span; {GreedyRule}. Counting: {CountingRule}.\n");

        text.Append("\n## Gold requirements and the claim that matched each\n\n");
        text.Append("Each cell names the claim that matched the gold requirement at 50%: its number in the arm's answer, or for\n");
        text.Append("B delivered its requirement ID. A dash means no claim of that arm matched it.\n\n");
        text.Append("| Gold | Section | Lines | Modality | Testability | ");
        text.Append(string.Join(" | ", scored.Select(view => view.Label)));
        text.Append(" | Quote |\n|---|---|---|---|---|");
        text.Append(string.Concat(scored.Select(_ => "---|")));
        text.Append("---|\n");

        for (var index = 0; index < gold.Requirements.Count; index++)
        {
            var requirement = gold.Requirements[index];

            text.Append(
                $"| {GoldReference.Label(requirement)} | {requirement.Section} | {Lines(quality.GoldPositions[index])} "
                + $"| {RunArtifacts.Spell(requirement.Modality)} | {RunArtifacts.Spell(requirement.Testability)} | ");
            text.Append(string.Join(" | ", scored.Select(view =>
                view.View.Primary.Match.ForGold(index) is { } match ? view.View.Predictions[match.Prediction].Label : "—")));
            text.Append($" | {Cell(requirement.Quote)} |\n");
        }

        text.Append("\n## Claims, arm by arm\n");

        foreach (var view in scored)
        {
            text.Append($"\n### {view.Label}\n\n");
            text.Append("| Claim | Quote found | Section | Modality | Matched gold | Overlap of the shorter span | Quote |\n");
            text.Append("|---|---|---|---|---|---:|---|\n");

            for (var index = 0; index < view.View.Predictions.Count; index++)
            {
                var prediction = view.View.Predictions[index];
                var match = view.View.Primary.Match.ForPrediction(index);
                var overlap = match is null
                    ? "—"
                    : Percent((double)match.Overlap / Math.Min(match.Span.Length, gold.Requirements[match.Gold].Span.Length));

                text.Append(
                    $"| {prediction.Label} | {Found(prediction.Outcome)} | {Sections(prediction)} "
                    + $"| {(prediction.Prediction.Modality is { } modality ? RunArtifacts.Spell(modality) : "—")} "
                    + $"| {(match is null ? "—" : GoldReference.Label(gold.Requirements[match.Gold]))} | {overlap} "
                    + $"| {Cell(prediction.Quote ?? "(no quote)")} |\n");
            }
        }

        text.Append($"\n{UnmatchedHeading}\n\n");
        text.Append("Claims that matched no gold requirement at 50%, located or not. The error analysis carries one verdict\n");
        text.Append("field for each of these rows.\n\n");
        text.Append("| Arm | Claim | Section | Quote |\n|---|---|---|---|\n");

        var unmatched = 0;

        foreach (var view in scored)
        {
            foreach (var index in view.View.Primary.Match.UnmatchedPredictions)
            {
                var prediction = view.View.Predictions[index];
                text.Append($"| {view.Label} | {prediction.Label} | {Sections(prediction)} | {Cell(prediction.Quote ?? "(no quote)")} |\n");
                unmatched++;
            }
        }

        if (unmatched == 0)
        {
            text.Append("| — | — | — | none |\n");
        }

        text.Append("\n## Gold requirements no claim matched\n\n");
        text.Append("| Arm | Gold | Section | Lines | Quote |\n|---|---|---|---|---|\n");

        foreach (var view in scored)
        {
            foreach (var index in view.View.Primary.Match.MissedGold)
            {
                var requirement = gold.Requirements[index];
                text.Append(
                    $"| {view.Label} | {GoldReference.Label(requirement)} | {requirement.Section} "
                    + $"| {Lines(quality.GoldPositions[index])} | {Cell(requirement.Quote)} |\n");
            }
        }

        text.Append("\n## Matching at 30% and 70% against 50%\n\n");
        text.Append("Pairs that exist at one threshold and not at 50%. Nothing else in the matching changes.\n\n");
        text.Append("| Arm | Threshold | Pairs gained | Pairs lost | Smallest overlap of a matched pair at 50% |\n|---|---:|---|---|---:|\n");

        foreach (var view in scored)
        {
            var primary = Pairs(view.View.Primary.Match);

            foreach (var threshold in view.View.Thresholds.Where(threshold => threshold.MinimumOverlapPercent != ArmQuality.MinimumOverlapPercent))
            {
                var pairs = Pairs(threshold.Match);

                text.Append(
                    $"| {view.Label} | {threshold.MinimumOverlapPercent}% | {PairList(view, gold, pairs.Except(primary))} "
                    + $"| {PairList(view, gold, primary.Except(pairs))} | {Percent(SmallestOverlap(view))} |\n");
            }
        }

        text.Append("\n## Recall by third of the document\n\n");
        text.Append($"The chunking rule and its decision are in [{ChunkingFile}]({ChunkingFile}).\n\n");
        RecallByThird(text, scored);

        text.Append("\n## Cost of verification\n\n");
        text.Append($"Similarity: {SimilarityRule}. Threshold: {Threshold()}. Both were fixed before the analysis was run.\n\n");
        text.Append("A quote *counts* as reaching a gold requirement when its similarity is at or above the threshold, the\n");
        text.Append("closest stretch overlaps that gold requirement by at least 50% of the shorter span, and no located claim\n");
        text.Append("of the same arm matched it. Every quote that cannot be located is listed, whether or not it counts.\n\n");
        text.Append("| Arm | Claim | Similarity | Edits | Closest stretch in section | Closest gold | Counts | Quote | Text at the closest stretch |\n");
        text.Append("|---|---|---:|---:|---|---|---|---|---|\n");

        var listed = 0;

        foreach (var view in scored)
        {
            var cost = view.View.Cost;

            for (var index = 0; index < cost.Unlocated.Count; index++)
            {
                var reach = cost.Unlocated[index];
                var place = view.View.Places[index];
                var prediction = view.View.Predictions[reach.Prediction];

                text.Append(
                    $"| {view.Label} | {prediction.Label} | {reach.Location.Similarity.ToString("0.000", CultureInfo.InvariantCulture)} "
                    + $"| {reach.Location.Distance} | {(place.Sections.Count == 0 ? "—" : string.Join(", ", place.Sections))} "
                    + $"| {(reach.ClosestGold.Count == 0 ? "none" : string.Join(", ", reach.ClosestGold.Select(closest => GoldReference.Label(gold.Requirements[closest]))))} "
                    + $"| {Counts(view, gold, reach)} | {Cell(prediction.Quote!)} | {Cell(place.Text)} |\n");
                listed++;
            }
        }

        if (listed == 0)
        {
            text.Append("| — | — | — | — | — | — | — | no quote failed to locate | — |\n");
        }

        var withoutQuote = scored.Where(view => view.View.ClaimsWithoutQuote > 0).ToList();

        foreach (var view in withoutQuote)
        {
            text.Append($"\n{view.Label}: {view.View.ClaimsWithoutQuote} claims without a quote, so there is no text to compare.\n");
        }

        foreach (var view in scored.Where(view => view.Delivered))
        {
            text.Append($"\n### {view.Label}: gold requirements B raw reached that are held back from the register\n\n");
            text.Append("| Gold | Section | Claim in B raw | Why it is not in the register | Quote |\n|---|---|---|---|---|\n");

            foreach (var held in view.Metrics.Quality!.HeldBack)
            {
                var requirement = gold.Requirements[held.Gold];
                text.Append(
                    $"| {GoldReference.Label(requirement)} | {requirement.Section} | {held.Claim} | {Reason(held.Reason)} "
                    + $"| {Cell(requirement.Quote)} |\n");
            }

            if (view.Metrics.Quality!.HeldBack.Count == 0)
            {
                text.Append("| — | — | — | none | — |\n");
            }
        }

        return text.ToString();
    }

    public static string RenderChunking(IReadOnlyList<HeadlineRow> rows, string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var scored = ScoredRows(rows);
        var text = new StringBuilder();

        text.Append("# Chunking decision (REQ-EXT-03)\n\n");
        Generated(text, command);
        text.Append("**The rule, fixed before the measurement:** chunked extraction is needed if the pipeline's recall in the\n");
        text.Append($"last third of the document is at least {PositionRecall.ChunkingThresholdPoints} percentage points below its recall in the first third.\n");
        text.Append("Recall here is B raw: every entry the extraction call returned, matched at 50% overlap, before\n");
        text.Append("verification holds anything back, because the question is whether the model reads the end of the\n");
        text.Append("document as well as its start. The document is split into thirds by line, and each gold requirement\n");
        text.Append("belongs to the third that holds the first line of its span.\n");

        foreach (var document in scored.Select(view => view.Metrics.DocumentId).Distinct())
        {
            var views = scored.Where(view => view.Metrics.DocumentId == document).ToList();
            var quality = views[0].Metrics.Quality!;
            var decisive = views.Single(view => view.Metrics.Arm == Arm.Pipeline && !view.Delivered).View.Thirds;
            var outcome = PositionRecall.Chunking(decisive[0], decisive[^1]);
            var first = quality.GoldPositions.Min(position => position.FirstLine);
            var last = quality.GoldPositions.Max(position => position.LastLine);
            var tokens = views.First(view => view.Metrics.Arm == Arm.Pipeline).Metrics.Cost?.WholeDocumentInputTokens;

            text.Append($"\n## {document}\n\n");
            text.Append("| Third | Lines | Gold requirements | ");
            text.Append(string.Join(" | ", views.Select(view => $"{view.Label}: matched, recall")));
            text.Append(" |\n|---|---|---:|");
            text.Append(string.Concat(views.Select(_ => "---:|")));
            text.Append('\n');

            for (var part = 0; part < decisive.Count; part++)
            {
                text.Append($"| {decisive[part].Number} | {decisive[part].FirstLine}–{decisive[part].LastLine} | {decisive[part].Gold} | ");
                text.Append(string.Join(" | ", views.Select(view =>
                    $"{view.View.Thirds[part].Matched} of {view.View.Thirds[part].Gold}, {Percent(view.View.Thirds[part].Recall)}")));
                text.Append(" |\n");
            }

            text.Append($"\n**Decision: {Decision(outcome)}**\n\n");
            text.Append(Explanation(outcome, decisive));
            text.Append("\n\n");
            text.Append($"- **Numbers this small are noise.** The thirds hold {string.Join(", ", decisive.Select(part => part.Gold))} gold requirements. ");
            text.Append(string.Join(" ", decisive.Where(part => part.Gold > 0).Select(part =>
                $"In third {part.Number}, one requirement moves recall by {Points(1.0 / part.Gold)} percentage points.")));
            text.Append(" A difference of one or two requirements between thirds says nothing about the model.\n");
            text.Append(
                $"- **This check cannot see degradation late in a long document.** All of {document}'s gold requirements sit in "
                + $"lines {first}–{last} of {quality.DocumentLines}");
            text.Append(tokens is { } count
                ? $", and the whole document is {count.ToString("N0", CultureInfo.InvariantCulture)} input tokens in the pipeline's extraction call. "
                : ". ");
            text.Append("A model that loses attention only after tens of thousands of tokens would pass it. The question stays ");
            text.Append("open for longer documents, and only a gold standard on a longer document could answer it.\n");
        }

        return text.ToString();
    }

    public static string Share(int numerator, int denominator) =>
        denominator == 0
            ? "—"
            : $"{ExperimentArtifacts.Percent((double)numerator / denominator)} ({numerator}/{denominator})";

    private static IEnumerable<Scored> Views(ArmMetrics metrics)
    {
        var quality = metrics.Quality!;

        yield return new Scored(metrics, quality.Claimed, Label(metrics.Arm, delivered: false), Delivered: false);

        if (quality.Delivered is { } delivered)
        {
            yield return new Scored(metrics, delivered, Label(metrics.Arm, delivered: true), Delivered: true);
        }
    }

    private static string HeadlineLabel(Scored view) =>
        view.Metrics.Arm == Arm.Chat ? "A0 chat — illustrative, not reproducible" : view.Label;

    private static double? SmallestOverlap(Scored view)
    {
        var gold = view.Metrics.Quality!.Gold.Requirements;
        var matches = view.View.Primary.Match.Matches;

        return matches.Count == 0
            ? null
            : matches.Min(match => (double)match.Overlap / Math.Min(match.Span.Length, gold[match.Gold].Span.Length));
    }

    private static string Points(double share) => (share * 100).ToString("0.0", CultureInfo.InvariantCulture);

    private static string Label(Arm arm, bool delivered) => arm switch
    {
        Arm.Chat => "A0 chat (illustrative)",
        Arm.Baseline => "A baseline",
        Arm.Pipeline when delivered => "B delivered",
        Arm.Pipeline => "B raw",
        _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, null),
    };

    private static void Generated(StringBuilder text, string command)
    {
        text.Append("This file is generated, offline, from `cache/`, the transcripts in `experiments/a0/` and the gold standard,\n");
        text.Append("by the command below. CI runs the same command and fails if the result differs from what is committed.\n");
        text.Append("Do not edit it by hand.\n\n");
        text.Append("```\n").Append(command).Append("\n```\n\n");
    }

    private static void RecallByThird(StringBuilder text, IReadOnlyList<Scored> scored)
    {
        var parts = scored[0].View.Thirds;

        text.Append("| Third | Lines | Gold requirements | ");
        text.Append(string.Join(" | ", scored.Select(view => view.Label)));
        text.Append(" |\n|---|---|---:|");
        text.Append(string.Concat(scored.Select(_ => "---:|")));
        text.Append('\n');

        for (var part = 0; part < parts.Count; part++)
        {
            text.Append($"| {parts[part].Number} | {parts[part].FirstLine}–{parts[part].LastLine} | {parts[part].Gold} | ");
            text.Append(string.Join(" | ", scored.Select(view =>
                $"{Percent(view.View.Thirds[part].Recall)} ({view.View.Thirds[part].Matched}/{view.View.Thirds[part].Gold})")));
            text.Append(" |\n");
        }
    }

    private static string Decision(ChunkingOutcome outcome) => outcome switch
    {
        ChunkingOutcome.Needed => "the rule fires; chunked extraction becomes a follow-up task under REQ-EXT-03.",
        ChunkingOutcome.NotNeeded => "the rule does not fire; single-request extraction stays, under REQ-EXT-03.",
        ChunkingOutcome.Undetermined => "the rule cannot fire on this document, so chunking is not added; single-request extraction stays, under REQ-EXT-03.",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    private static string Explanation(ChunkingOutcome outcome, IReadOnlyList<DocumentPart> parts)
    {
        var first = parts[0];
        var last = parts[^1];

        return outcome switch
        {
            ChunkingOutcome.Undetermined =>
                $"The {(first.Gold == 0 ? "first" : "last")} third holds no gold requirement, so its recall is undefined and "
                + "the comparison the rule needs cannot be made. This is an absence of evidence, not evidence that recall "
                + "holds up late in a document.",
            _ =>
                $"Recall is {Percent(first.Recall)} in the first third and {Percent(last.Recall)} in the last, a difference of "
                + $"{((first.Recall!.Value - last.Recall!.Value) * 100).ToString("0.0", CultureInfo.InvariantCulture)} percentage points "
                + $"against the rule's {PositionRecall.ChunkingThresholdPoints}.",
        };
    }

    private static string Modality(QualityScore score) =>
        !score.ModalityStated
            ? "n/a: the arm states no modality"
            : score.ModalityCorrect is { } correct && score.Matched > 0
                ? Share(correct, score.Matched)
                : "—";

    private static string Found(ClaimOutcome outcome) => outcome switch
    {
        ClaimOutcome.FoundOnce => "once",
        ClaimOutcome.FoundMoreThanOnce => "more than once",
        ClaimOutcome.NotFound => "not found",
        ClaimOutcome.WithoutQuote => "no quote",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    private static string Reason(string reason) => reason switch
    {
        QueuedDecision.QuoteFoundMoreThanOnce => "its quote is found more than once, so no single span can be claimed",
        QueuedDecision.ConflictingReadings => "the same quote was claimed twice with different modalities",
        HeldBack.RegisterEntryMatchedElsewhere => "its register entry matched another gold requirement",
        HeldBack.NotInRegister => "not in the register",
        _ => reason,
    };

    private static string Counts(Scored view, GoldReference gold, UnlocatedReach reach)
    {
        if (reach.CountedGold is { } counted)
        {
            return $"yes: {GoldReference.Label(gold.Requirements[counted])}";
        }

        if (!reach.AtOrAboveThreshold)
        {
            return "no: below the threshold";
        }

        if (reach.ClosestGold.Count == 0)
        {
            return "no: no gold requirement there";
        }

        return string.Join("; ", reach.ClosestGold.Select(closest =>
            view.View.Primary.Match.ForGold(closest) is { } match
                ? $"no: claim {view.View.Predictions[match.Prediction].Label} matched {GoldReference.Label(gold.Requirements[closest])}"
                : $"no: {GoldReference.Label(gold.Requirements[closest])} is counted for another quote"));
    }

    private static HashSet<(int Prediction, int Gold)> Pairs(MatchResult match) =>
        [.. match.Matches.Select(pair => (pair.Prediction, pair.Gold))];

    private static string PairList(Scored view, GoldReference gold, IEnumerable<(int Prediction, int Gold)> pairs)
    {
        var listed = pairs
            .OrderBy(pair => pair.Gold)
            .Select(pair => $"{view.View.Predictions[pair.Prediction].Label} → {GoldReference.Label(gold.Requirements[pair.Gold])}")
            .ToList();

        return listed.Count == 0 ? "none" : string.Join(", ", listed);
    }

    private static string Labels(ArmQuality quality, IEnumerable<int> gold)
    {
        var labels = gold.Select(index => GoldReference.Label(quality.Gold.Requirements[index])).ToList();

        return labels.Count == 0 ? "none" : string.Join(", ", labels);
    }

    private static string Sections(ScoredPrediction prediction) => string.Join(", ", prediction.Sections);

    private static string Lines(GoldPosition position) =>
        position.FirstLine == position.LastLine
            ? position.FirstLine.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{position.FirstLine}–{position.LastLine}");

    private static string Cell(string text) => TextNormalizer.Normalize(text).Replace("|", "\\|", StringComparison.Ordinal);

    private static string Percent(double? share) => ExperimentArtifacts.Percent(share);

    private static string Threshold() => (ArmQuality.SimilarityThresholdPercent / 100.0).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Short(string commit) => commit.Length > 7 ? commit[..7] : commit;

    public sealed record Scored(ArmMetrics Metrics, QualityView View, string Label, bool Delivered);
}
