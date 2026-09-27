namespace SpecTrace.Core.Tests;

public sealed class KeywordSentencesTests
{
    [Fact]
    public void EverySentenceWithAnUppercaseKeywordIsListedWithTheKeywordsItCarriesInOrder()
    {
        var found = KeywordSentences.Find(Text("""
            1.  Rules

               A client MUST send a name.  A server SHOULD answer, and it MAY
               close.  Nothing here is normative.  A proxy SHALL NOT cache.

               A field is REQUIRED.  A flag is OPTIONAL.  Retries are
               RECOMMENDED.  Silence is NOT RECOMMENDED.  A header SHALL be
               sent, and a body SHOULD NOT be.
            """));

        Assert.Equal(
            [
                "A client MUST send a name.",
                "A server SHOULD answer, and it MAY close.",
                "A proxy SHALL NOT cache.",
                "A field is REQUIRED.",
                "A flag is OPTIONAL.",
                "Retries are RECOMMENDED.",
                "Silence is NOT RECOMMENDED.",
                "A header SHALL be sent, and a body SHOULD NOT be.",
            ],
            found.Select(sentence => sentence.Sentence));

        Assert.Equal(["SHOULD", "MAY"], found[1].Keywords);
        Assert.Equal(["SHALL NOT"], found[2].Keywords);
        Assert.Equal(["NOT RECOMMENDED"], found[6].Keywords);
        Assert.Equal(["SHALL", "SHOULD NOT"], found[7].Keywords);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], found.Select(sentence => sentence.Number));
    }

    [Fact]
    public void LowercaseCapitalisedAndLongerWordsAreNotKeywords()
    {
        var found = KeywordSentences.Find(Text("""
            1.  Prose

               A client must send a name.  May it close?  Required parameters: none.
               MUSTARD is a condiment.  MAYBE later.  Should it?
            """));

        Assert.Empty(found);
    }

    [Fact]
    public void AKeywordBrokenAcrossALineIsReadAsOneKeyword()
    {
        var found = KeywordSentences.Find(Text("""
            1.  Rules

               The index MUST
               NOT be negative.
            """));

        var sentence = Assert.Single(found);
        Assert.Equal(["MUST NOT"], sentence.Keywords);
        Assert.Equal("The index MUST NOT be negative.", sentence.Sentence);
    }

    [Fact]
    public void ASentenceThatContinuesAcrossAPageBreakIsOneSentenceWithoutTheFooterOrHeader()
    {
        var raw = Text("""
            1.  Rules

               The client MUST send the header

            Doe                          Standards Track                    [Page 1]
            {FF}
            RFC 9999                       Example                     April 2013


               before the body.  Then it waits.
            """).Replace("{FF}", "\f", StringComparison.Ordinal);

        var sentence = Assert.Single(KeywordSentences.Find(raw));

        Assert.Equal("The client MUST send the header before the body.", sentence.Sentence);
        Assert.Equal(["The client MUST send the header", "before the body.  Then it waits."], sentence.SourceLines);
        Assert.Equal(3, sentence.FirstLine);
        Assert.Equal(10, sentence.LastLine);
    }

    [Fact]
    public void ABlankLineEndsASentenceThatHasNoFullStop()
    {
        var found = KeywordSentences.Find(Text("""
            1.  Rules

               The target MUST reference one of:

               o  a member MAY be added.
            """));

        Assert.Equal(
            ["The target MUST reference one of:", "o a member MAY be added."],
            found.Select(sentence => sentence.Sentence));
    }

    [Fact]
    public void AnAbbreviationFollowedByACommaDoesNotEndASentence()
    {
        var sentence = Assert.Single(KeywordSentences.Find(Text("""
            1.  Rules

               The member MUST be ignored (i.e., the operation completes, e.g., as usual).
            """)));

        Assert.Equal("The member MUST be ignored (i.e., the operation completes, e.g., as usual).", sentence.Sentence);
    }

    [Fact]
    public void EachSentenceNamesItsSectionItsLinesAndTheSpanOfItsRawText()
    {
        var raw = Text("""
            1.  First

               Prose.

            2.  Second

               The server MUST
               answer.
            """);

        var sentence = Assert.Single(KeywordSentences.Find(raw));

        Assert.Equal("2", sentence.Section);
        Assert.Equal(7, sentence.FirstLine);
        Assert.Equal(8, sentence.LastLine);
        Assert.Equal("The server MUST\n   answer.", raw[sentence.Span.Start..sentence.Span.End]);
    }

    private static string Text(string text) => text.ReplaceLineEndings("\n") + "\n";
}
