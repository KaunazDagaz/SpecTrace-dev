namespace SpecTrace.Core.Tests;

public sealed class GoldCheckTests
{
    private const string DocumentId = "widgets";

    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private static readonly string Raw = """
        1.  Widgets

           A widget MUST have a name.  A widget MAY carry a colour, and it
           SHOULD NOT carry two.

        2.  Gadgets

           The gadget MUST exist before use.

        3.  Gizmos

           The gadget MUST exist before use.  Gizmos are
           RECOMMENDED for testing.
        """.ReplaceLineEndings("\n") + "\n";

    private static readonly IReadOnlyList<KeywordSentence> Scan = KeywordSentences.Find(Raw);

    [Fact]
    public void AFileWhoseEveryQuoteIsFoundExactlyOnceLoadsWithEachSectionDerivedFromItsSpan()
    {
        var result = Check(Valid());

        Assert.Empty(result.Problems);
        var standard = Assert.IsType<GoldStandard>(result.Standard);
        Assert.Equal(5, standard.Requirements.Count);
        Assert.Equal(["1", "1", "1", "2", "3"], standard.Requirements.Select(requirement => requirement.Section));
        Assert.Equal(
            [Modality.Must, Modality.May, Modality.ShouldNot, Modality.Must, Modality.Must],
            standard.Requirements.Select(requirement => requirement.Modality));
        Assert.All(standard.Requirements, requirement => Assert.Equal(
            TextNormalizer.Normalize(requirement.Quote),
            TextNormalizer.Normalize(Raw[requirement.Span.Start..requirement.Span.End])));
        Assert.Equal(Commit, standard.RulesCommit);
        Assert.Equal((4, 1, 0), (result.Kept, result.Dropped, result.Undecided));
    }

    [Fact]
    public void AQuoteNotInTheDocumentRejectsTheWholeFileAndSaysWhereItDeparts()
    {
        var result = Check(WithObligation(Valid(), 1, 0, obligation => obligation with { Quote = "A widget MUST have a title" }));

        Assert.Null(result.Standard);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(11, problem.Line);
        Assert.StartsWith("candidate 1, obligation 1: the quote is not in the document", problem.Message, StringComparison.Ordinal);
        Assert.Contains("the quote has \"title\" where the document has \"name.", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AQuoteFoundMoreThanOnceWithNoSectionIsRejectedAndItsSectionsAreNamed()
    {
        var result = Check(WithObligation(Valid(), 3, 0, obligation => obligation with { Section = "" }));

        Assert.Null(result.Standard);
        Assert.Contains(
            "candidate 3, obligation 1: the quote occurs 2 times in the document, in sections 2, 3; name the section it sits in",
            Messages(result));
    }

    [Fact]
    public void ANamedSectionResolvesARepeatedQuoteToItsOccurrenceInThatSection()
    {
        var requirements = Check(Valid()).Requirements;
        var inGadgets = requirements.Single(requirement => requirement.Candidate == 3);
        var inGizmos = requirements.Single(requirement => requirement.Candidate == 4);

        Assert.Equal("2", inGadgets.Section);
        Assert.Equal("3", inGizmos.Section);
        Assert.True(inGadgets.Span.End <= inGizmos.Span.Start);
    }

    [Fact]
    public void AQuoteAbsentFromItsNamedSectionIsRejectedAndWhereItOccursIsNamed()
    {
        var result = Check(WithObligation(Valid(), 1, 0, obligation => obligation with { Section = "2" }));

        Assert.Contains("candidate 1, obligation 1: the quote is not in section 2; it occurs in section 1", Messages(result));
    }

    [Fact]
    public void ASectionTheDocumentDoesNotHaveIsRejected()
    {
        var result = Check(WithObligation(Valid(), 1, 0, obligation => obligation with { Section = "9" }));

        Assert.Contains("candidate 1, obligation 1: section '9' is not a section of this document", Messages(result));
    }

    [Fact]
    public void AQuoteOccurringTwiceInItsNamedSectionIsRejected()
    {
        var result = Check(WithObligation(Valid(), 1, 0, obligation => obligation with { Quote = "A widget", Section = "1" }));

        Assert.Contains(
            "candidate 1, obligation 1: the quote occurs more than once in section 1; extend it within its sentence until it occurs there once",
            Messages(result));
    }

    [Theory]
    [InlineData("SHALL")]
    [InlineData("MUST NOT")]
    [InlineData("must")]
    [InlineData("RECOMMENDED")]
    public void AModalityOtherThanTheFiveClassesIsRejected(string modality)
    {
        var result = Check(WithObligation(Valid(), 1, 0, obligation => obligation with { Modality = modality }));

        Assert.Null(result.Standard);
        Assert.Contains(
            $"candidate 1, obligation 1: modality '{modality}' is not one of MUST, MUST_NOT, SHOULD, SHOULD_NOT, MAY",
            Messages(result));
    }

    [Fact]
    public void AnEmptyOrMisspelledTestabilityIsRejected()
    {
        var file = WithObligation(Valid(), 1, 0, obligation => obligation with { Testability = "" });
        file = WithObligation(file, 2, 0, obligation => obligation with { Testability = "Testable" });

        var messages = Messages(Check(file));

        Assert.Contains("candidate 1, obligation 1: testability is empty; it is one of testable, needs_human_decision, not_testable", messages);
        Assert.Contains("candidate 2, obligation 1: testability 'Testable' is not one of testable, needs_human_decision, not_testable", messages);
    }

    [Fact]
    public void AnObligationWithAnEmptyQuoteIsRejected()
    {
        var result = Check(WithObligation(Valid(), 1, 0, obligation => obligation with { Quote = " " }));

        Assert.Contains("candidate 1, obligation 1: the quote is empty", Messages(result));
    }

    [Fact]
    public void EveryCandidateNeedsAKeepOrDropDecision()
    {
        var file = WithCandidate(Valid(), 1, candidate => candidate with { Decision = "" });
        file = WithCandidate(file, 2, candidate => candidate with { Decision = "maybe" });

        var result = Check(file);

        Assert.Contains("candidate 1 has no decision: keep or drop", Messages(result));
        Assert.Contains("candidate 2: decision 'maybe' is neither keep nor drop", Messages(result));
        Assert.Equal((2, 1, 2), (result.Kept, result.Dropped, result.Undecided));
    }

    [Fact]
    public void AKeptCandidateMustRecordAnObligationAndADroppedOneMustNot()
    {
        var file = WithCandidate(Valid(), 1, candidate => candidate with { Obligations = [Blank(12)] });
        file = WithCandidate(file, 5, candidate => candidate with { Obligations = [Obligation(60, "Gizmos are RECOMMENDED", "SHOULD", "testable")] });

        var messages = Messages(Check(file));

        Assert.Contains("candidate 1 is kept but records no obligation", messages);
        Assert.Contains("candidate 5 is dropped but records 1 obligation", messages);
    }

    [Fact]
    public void TheTemplatesEmptyObligationUnderADroppedCandidateIsIgnored()
    {
        var result = Check(Valid());

        Assert.Empty(result.Problems);
        Assert.DoesNotContain(result.Requirements, requirement => requirement.Candidate == 5);
    }

    [Fact]
    public void TwoEntriesQuotingTheSamePlaceAreRejected()
    {
        var file = WithCandidate(Valid(), 1, candidate => candidate with
        {
            Obligations = [candidate.Obligations[0], candidate.Obligations[0] with { Line = 16 }],
        });

        Assert.Contains(
            "candidate 1, obligation 1 and candidate 1, obligation 2 quote the same place in the document",
            Messages(Check(file)));
    }

    [Fact]
    public void EverySentenceTheKeywordScanFindsMustBeInTheFileUnchanged()
    {
        var file = Valid() with { Candidates = [.. Valid().Candidates.Where(candidate => candidate.Number != 5)] };
        file = WithCandidate(file, 2, candidate => candidate with { Sentence = "A widget MAY carry a colour." });

        var messages = Messages(Check(file));

        Assert.Contains(
            "candidate 5 (section 3, line 12 of the document) is missing from the file; every sentence the keyword scan finds needs a decision",
            messages);
        Assert.Contains(
            "candidate 2's sentence is not the one the keyword scan finds; the generated fields of the worksheet are copied, never edited",
            messages);
    }

    [Fact]
    public void ACandidateTheKeywordScanDoesNotFindOrOneListedTwiceIsRejected()
    {
        var file = Valid() with
        {
            Candidates = [.. Valid().Candidates, Valid().Candidates[0] with { Line = 70 }, Valid().Candidates[0] with { Line = 80, Number = 6 }],
        };

        var messages = Messages(Check(file));

        Assert.Contains("candidate 1 appears more than once in the file", messages);
        Assert.Contains("candidate 6 is not a sentence the keyword scan finds in this document", messages);
    }

    [Fact]
    public void AFileForAnotherDocumentIsRejected()
    {
        var result = Check(Valid() with { Document = "rfc6901" });

        Assert.Contains("document is 'rfc6901', but the document checked is 'widgets'", Messages(result));
    }

    [Fact]
    public void AFileNamingNoRulesCommitOrAnotherOneIsRejected()
    {
        Assert.Contains(
            $"annotation_rules_commit is empty; the frozen rules are spectrace-docs commit {Commit}",
            Messages(Check(Valid() with { RulesCommit = "" })));

        Assert.Contains(
            $"annotation_rules_commit is 0123456, but the frozen rules are spectrace-docs commit {Commit}",
            Messages(Check(Valid() with { RulesCommit = "0123456" })));

        Assert.Empty(Check(Valid() with { RulesCommit = Commit.ToUpperInvariant() }).Problems);
    }

    [Fact]
    public void NoFileLoadsWhileNoFrozenRulesCommitIsRecorded()
    {
        var result = GoldCheck.Run(Valid(), DocumentId, Raw, frozenRulesCommit: "");

        Assert.Null(result.Standard);
        Assert.Contains(
            "no frozen annotation rules commit is recorded in spectrace-dev yet, so no gold file loads; "
            + "it is recorded once the rules' pull request merges in spectrace-docs",
            Messages(result));
    }

    [Fact]
    public void EveryProblemIsReportedRatherThanOnlyTheFirst()
    {
        var file = WithObligation(Valid(), 1, 0, obligation => obligation with { Quote = "absent" });
        file = WithObligation(file, 2, 1, obligation => obligation with { Modality = "SHALL" });
        file = file with { RulesCommit = "" };

        var result = Check(file);

        Assert.Equal(3, result.Problems.Count);
        Assert.Equal([0, 11, 26], result.Problems.Select(problem => problem.Line));
    }

    private static GoldCheckResult Check(AnnotatedFile file) => GoldCheck.Run(file, DocumentId, Raw, Commit);

    private static List<string> Messages(GoldCheckResult result) => [.. result.Problems.Select(problem => problem.Message)];

    private static AnnotatedFile Valid() => new(
        DocumentId,
        Commit,
        "Annotator",
        "2026-09-28",
        [
            Candidate(1, 5, GoldCheck.Keep, Obligation(11, "A widget MUST have a name", "MUST", "testable")),
            Candidate(2, 16, GoldCheck.Keep,
                Obligation(21, "A widget MAY carry a colour", "MAY", "testable"),
                Obligation(26, "it SHOULD NOT carry two", "SHOULD_NOT", "needs_human_decision")),
            Candidate(3, 31, GoldCheck.Keep, Obligation(36, "The gadget MUST exist before use", "MUST", "testable", "2")),
            Candidate(4, 41, GoldCheck.Keep, Obligation(46, "The gadget MUST exist before use", "MUST", "testable", "3")),
            Candidate(5, 51, GoldCheck.Drop, Blank(56)),
        ]);

    private static AnnotatedCandidate Candidate(int number, int line, string decision, params AnnotatedObligation[] obligations) =>
        new(line, number, Scan[number - 1].Sentence, decision, obligations);

    private static AnnotatedObligation Obligation(int line, string quote, string modality, string testability, string section = "") =>
        new(line, quote, modality, testability, section);

    private static AnnotatedObligation Blank(int line) => new(line, "", "", "", "");

    private static AnnotatedFile WithCandidate(AnnotatedFile file, int number, Func<AnnotatedCandidate, AnnotatedCandidate> change) =>
        file with
        {
            Candidates = [.. file.Candidates.Select(candidate => candidate.Number == number ? change(candidate) : candidate)],
        };

    private static AnnotatedFile WithObligation(
        AnnotatedFile file,
        int number,
        int index,
        Func<AnnotatedObligation, AnnotatedObligation> change) =>
        WithCandidate(file, number, candidate => candidate with
        {
            Obligations = [.. candidate.Obligations.Select((obligation, at) => at == index ? change(obligation) : obligation)],
        });
}
