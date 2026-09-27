namespace SpecTrace.Core;

public sealed record GoldProblem(int Line, string Message);

public sealed record GoldCheckResult
{
    internal GoldCheckResult(
        AnnotatedFile file,
        IReadOnlyList<GoldRequirement> requirements,
        IReadOnlyList<GoldProblem> problems,
        int kept,
        int dropped,
        int undecided)
    {
        Requirements = [.. requirements];
        Problems = [.. problems];
        Kept = kept;
        Dropped = dropped;
        Undecided = undecided;
        Standard = problems.Count == 0
            ? new GoldStandard(file.Document, file.RulesCommit, file.Annotator, file.AnnotatedAt, requirements)
            : null;
    }

    public IReadOnlyList<GoldRequirement> Requirements { get; }

    public IReadOnlyList<GoldProblem> Problems { get; }

    public int Kept { get; }

    public int Dropped { get; }

    public int Undecided { get; }

    public GoldStandard? Standard { get; }
}

public static class GoldCheck
{
    public const string Keep = "keep";

    public const string Drop = "drop";

    private const int ExcerptLength = 40;

    private static readonly Dictionary<string, Modality> Modalities = new(StringComparer.Ordinal)
    {
        ["MUST"] = Modality.Must,
        ["MUST_NOT"] = Modality.MustNot,
        ["SHOULD"] = Modality.Should,
        ["SHOULD_NOT"] = Modality.ShouldNot,
        ["MAY"] = Modality.May,
    };

    private static readonly Dictionary<string, Testability> Testabilities = new(StringComparer.Ordinal)
    {
        ["testable"] = Testability.Testable,
        ["needs_human_decision"] = Testability.NeedsHumanDecision,
        ["not_testable"] = Testability.NotTestable,
    };

    public static IReadOnlyList<string> ModalityValues { get; } = [.. Modalities.Keys];

    public static IReadOnlyList<string> TestabilityValues { get; } = [.. Testabilities.Keys];

    public static GoldCheckResult Run(AnnotatedFile file, string documentId, string raw, string frozenRulesCommit)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(frozenRulesCommit);

        var source = new Source(raw);
        var problems = new List<GoldProblem>();

        CheckHeader(file, documentId, frozenRulesCommit, problems);
        CheckCandidates(file, KeywordSentences.Find(raw), problems);

        var requirements = new List<GoldRequirement>();
        var kept = 0;
        var dropped = 0;
        var undecided = 0;

        foreach (var candidate in file.Candidates)
        {
            var recorded = candidate.Obligations.Count(obligation => !obligation.IsBlank);

            switch (candidate.Decision)
            {
                case Keep:
                    kept++;
                    if (recorded == 0)
                    {
                        problems.Add(new(candidate.Line, $"candidate {candidate.Number} is kept but records no obligation"));
                    }

                    break;

                case Drop:
                    dropped++;
                    if (recorded > 0)
                    {
                        problems.Add(new(candidate.Line, $"candidate {candidate.Number} is dropped but records {Count(recorded, "obligation")}"));
                    }

                    break;

                case "":
                    undecided++;
                    problems.Add(new(candidate.Line, $"candidate {candidate.Number} has no decision: {Keep} or {Drop}"));
                    break;

                default:
                    undecided++;
                    problems.Add(new(candidate.Line, $"candidate {candidate.Number}: decision '{candidate.Decision}' is neither {Keep} nor {Drop}"));
                    break;
            }

            for (var index = 0; index < candidate.Obligations.Count; index++)
            {
                var obligation = candidate.Obligations[index];

                if (obligation.IsBlank)
                {
                    continue;
                }

                var label = $"candidate {candidate.Number}, obligation {index + 1}";
                var requirement = Check(candidate.Number, index + 1, obligation, source, label, problems);

                if (requirement is not null && candidate.Decision != Drop)
                {
                    requirements.Add(requirement);
                }
            }
        }

        foreach (var samePlace in requirements.GroupBy(requirement => requirement.Span).Where(group => group.Count() > 1))
        {
            var labels = samePlace.Select(requirement => $"candidate {requirement.Candidate}, obligation {requirement.Obligation}");
            problems.Add(new(0, $"{string.Join(" and ", labels)} quote the same place in the document"));
        }

        return new GoldCheckResult(
            file,
            requirements,
            [.. problems.OrderBy(problem => problem.Line)],
            kept,
            dropped,
            undecided);
    }

    private static void CheckHeader(
        AnnotatedFile file,
        string documentId,
        string frozenRulesCommit,
        List<GoldProblem> problems)
    {
        if (file.Document != documentId)
        {
            problems.Add(new(0, $"document is '{file.Document}', but the document checked is '{documentId}'"));
        }

        if (frozenRulesCommit.Length == 0)
        {
            problems.Add(new(0,
                "no frozen annotation rules commit is recorded in spectrace-dev yet, so no gold file loads; "
                + "it is recorded once the rules' pull request merges in spectrace-docs"));
        }
        else if (file.RulesCommit.Length == 0)
        {
            problems.Add(new(0, $"annotation_rules_commit is empty; the frozen rules are spectrace-docs commit {frozenRulesCommit}"));
        }
        else if (!string.Equals(file.RulesCommit, frozenRulesCommit, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(new(0,
                $"annotation_rules_commit is {file.RulesCommit}, but the frozen rules are spectrace-docs commit {frozenRulesCommit}"));
        }
    }

    private static void CheckCandidates(
        AnnotatedFile file,
        IReadOnlyList<KeywordSentence> scan,
        List<GoldProblem> problems)
    {
        var byNumber = file.Candidates.ToLookup(candidate => candidate.Number);

        foreach (var repeated in byNumber.Where(group => group.Count() > 1))
        {
            problems.Add(new(repeated.Skip(1).First().Line, $"candidate {repeated.Key} appears more than once in the file"));
        }

        foreach (var sentence in scan)
        {
            var candidate = byNumber[sentence.Number].FirstOrDefault();

            if (candidate is null)
            {
                problems.Add(new(0,
                    $"candidate {sentence.Number} (section {sentence.Section}, line {sentence.FirstLine} of the document) "
                    + "is missing from the file; every sentence the keyword scan finds needs a decision"));
            }
            else if (TextNormalizer.Normalize(candidate.Sentence) != sentence.Sentence)
            {
                problems.Add(new(candidate.Line,
                    $"candidate {candidate.Number}'s sentence is not the one the keyword scan finds; "
                    + "the generated fields of the worksheet are copied, never edited"));
            }
        }

        foreach (var candidate in file.Candidates.Where(candidate => candidate.Number < 1 || candidate.Number > scan.Count))
        {
            problems.Add(new(candidate.Line, $"candidate {candidate.Number} is not a sentence the keyword scan finds in this document"));
        }
    }

    private static GoldRequirement? Check(
        int candidate,
        int obligation,
        AnnotatedObligation entry,
        Source source,
        string label,
        List<GoldProblem> problems)
    {
        var valid = true;

        if (!Modalities.TryGetValue(entry.Modality, out var modality))
        {
            problems.Add(new(entry.Line, $"{label}: {Invalid("modality", entry.Modality, ModalityValues)}"));
            valid = false;
        }

        if (!Testabilities.TryGetValue(entry.Testability, out var testability))
        {
            problems.Add(new(entry.Line, $"{label}: {Invalid("testability", entry.Testability, TestabilityValues)}"));
            valid = false;
        }

        var (span, failure) = source.Locate(entry.Quote, entry.Section);

        if (failure is not null)
        {
            problems.Add(new(entry.Line, $"{label}: {failure}"));
            return null;
        }

        return valid
            ? new GoldRequirement(candidate, obligation, entry.Quote, modality, testability, span, source.Sections.SectionFor(span))
            : null;
    }

    private static string Invalid(string field, string value, IReadOnlyList<string> allowed) =>
        value.Length == 0
            ? $"{field} is empty; it is one of {string.Join(", ", allowed)}"
            : $"{field} '{value}' is not one of {string.Join(", ", allowed)}";

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string Excerpt(string text, int from) =>
        text.Length - from <= ExcerptLength ? text[from..] : text.Substring(from, ExcerptLength) + "…";

    private sealed class Source
    {
        private readonly string _raw;

        public Source(string raw)
        {
            _raw = raw;
            Document = NormalizedDocument.Create(raw);
            Sections = SectionIndex.Build(raw);
        }

        public NormalizedDocument Document { get; }

        public SectionIndex Sections { get; }

        public (TextSpan Span, string? Failure) Locate(string quote, string section)
        {
            var normalized = TextNormalizer.Normalize(quote);

            if (normalized.Length == 0)
            {
                return (default, "the quote is empty");
            }

            if (section.Length == 0)
            {
                var resolution = Document.Resolve(quote);

                return resolution.Verification switch
                {
                    Verification.Exact => (resolution.Span!.Value, null),
                    Verification.Ambiguous => (default,
                        $"the quote occurs {Occurrences(normalized).Count} times in the document, in sections "
                        + $"{string.Join(", ", Occurrences(normalized).Distinct())}; name the section it sits in"),
                    _ => (default, NotFound(normalized)),
                };
            }

            var headers = Sections.Headers;
            var index = Enumerable.Range(0, headers.Count).FirstOrDefault(at => headers[at].Number == section, -1);

            if (index < 0)
            {
                return (default, $"section '{section}' is not a section of this document");
            }

            var start = headers[index].RawOffset;
            var end = index + 1 < headers.Count ? headers[index + 1].RawOffset : _raw.Length;
            var within = NormalizedDocument.Create(_raw[start..end]).Resolve(quote);

            switch (within.Verification)
            {
                case Verification.Exact:
                    var span = within.Span!.Value;
                    return (new TextSpan(start + span.Start, start + span.End), null);

                case Verification.Ambiguous:
                    return (default,
                        $"the quote occurs more than once in section {section}; extend it within its sentence until it occurs there once");

                default:
                    var elsewhere = Occurrences(normalized);
                    return (default, elsewhere.Count == 0
                        ? NotFound(normalized)
                        : $"the quote is not in section {section}; it occurs in section {string.Join(", ", elsewhere.Distinct())}");
            }
        }

        private List<string> Occurrences(string normalized)
        {
            var sections = new List<string>();

            for (var at = Document.Normal.IndexOf(normalized, StringComparison.Ordinal);
                at >= 0;
                at = Document.Normal.IndexOf(normalized, at + 1, StringComparison.Ordinal))
            {
                sections.Add(Sections.SectionFor(Document.Map[at]));
            }

            return sections;
        }

        private string NotFound(string normalized)
        {
            var low = 0;
            var high = normalized.Length;

            while (low < high)
            {
                var middle = low + ((high - low + 1) / 2);

                if (Document.Normal.Contains(normalized[..middle], StringComparison.Ordinal))
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            if (low == 0)
            {
                return "the quote is not in the document; not even its first character is";
            }

            var at = Document.Normal.IndexOf(normalized[..low], StringComparison.Ordinal);

            return $"the quote is not in the document: its first {low} of {normalized.Length} characters are, "
                + $"then the quote has \"{Excerpt(normalized, low)}\" where the document has \"{Excerpt(Document.Normal, at + low)}\"";
        }
    }
}
