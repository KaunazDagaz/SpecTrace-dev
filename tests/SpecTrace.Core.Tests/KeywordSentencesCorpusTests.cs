using System.Text.RegularExpressions;

namespace SpecTrace.Core.Tests;

public sealed partial class KeywordSentencesCorpusTests
{
    private static readonly IReadOnlyList<KeywordSentence> Found = KeywordSentences.Find(Corpus.Raw);

    [Fact]
    public void EveryUppercaseKeywordInTheRawFileFallsInsideExactlyOneListedSentence()
    {
        var keywords = KeywordWord().Matches(Corpus.Raw);

        Assert.NotEmpty(keywords);
        Assert.All(keywords, keyword => Assert.Single(
            Found,
            sentence => sentence.Span.Start <= keyword.Index && keyword.Index < sentence.Span.End));
    }

    [Fact]
    public void NoTwoListedSentencesOverlapAndTheyAreNumberedInDocumentOrder()
    {
        Assert.Equal(Enumerable.Range(1, Found.Count), Found.Select(sentence => sentence.Number));

        for (var index = 1; index < Found.Count; index++)
        {
            Assert.True(Found[index - 1].Span.End <= Found[index].Span.Start);
        }
    }

    [Fact]
    public void EveryListedSentenceIsTheRawTextAtItsSpanAndTheResolverFindsIt()
    {
        Assert.All(Found, sentence =>
        {
            Assert.Equal(sentence.Sentence, TextNormalizer.Normalize(Corpus.Raw[sentence.Span.Start..sentence.Span.End]));
            Assert.NotEqual(Verification.Failed, Corpus.Document.Resolve(sentence.Sentence).Verification);
            Assert.Equal(Corpus.Index.SectionFor(sentence.Span), sentence.Section);
        });
    }

    [Fact]
    public void NoListedSentenceOrSourceLineCarriesARunningPageHeaderOrFooter()
    {
        Assert.All(Found, sentence =>
        {
            Assert.DoesNotContain("[Page", sentence.Sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("JSON Patch April 2013", sentence.Sentence, StringComparison.Ordinal);
            Assert.DoesNotContain(sentence.SourceLines, line => line.Contains("[Page", StringComparison.Ordinal));
            Assert.DoesNotContain(sentence.SourceLines, line => line.StartsWith("RFC 6902 ", StringComparison.Ordinal));
        });
    }

    [GeneratedRegex(@"\b(?:MUST|SHALL|SHOULD|RECOMMENDED|REQUIRED|MAY|OPTIONAL)\b")]
    private static partial Regex KeywordWord();
}
