using System.Text.Json;
using SpecTrace.Core;
using SpecTrace.Llm;

namespace SpecTrace.Pipeline.Tests;

public sealed class ClaimParserTests
{
    [Fact]
    public void TheRealBaselineAnswerOnRfc6902SplitsIntoOneClaimPerRequirementHeading()
    {
        var answer = RealAnswers.Baseline(Corpus.Raw);
        var claims = ClaimParser.Parse(answer);
        var headings = answer.Split('\n').Count(line => line.StartsWith("### Requirement ", StringComparison.Ordinal));

        Assert.Equal(16, headings);
        Assert.Equal(headings, claims.Count);
        Assert.Equal(Enumerable.Range(1, 16).Select(number => $"### Requirement {number}"), claims.Select(claim => claim.Item));
        Assert.All(claims, claim => Assert.NotNull(claim.Quote));
    }

    [Fact]
    public void TheRealChatAnswerOnRfc6902SplitsIntoOneClaimPerNumberedHeading()
    {
        var answer = RealAnswers.Chat(Corpus.DocumentId);
        var claims = ClaimParser.Parse(answer);

        Assert.Equal(18, claims.Count);
        Assert.Equal("### 1. Operation Object 'op' Member", claims[0].Item);
        Assert.Equal("### 18. Error Handling and Termination", claims[^1].Item);
        Assert.All(claims, claim => Assert.NotNull(claim.Quote));
    }

    [Theory]
    [InlineData("baseline")]
    [InlineData("chat")]
    public void EveryQuoteTakenFromARealAnswerIsAVerbatimPartOfTheLineItCameFrom(string arm)
    {
        var answer = arm == "baseline" ? RealAnswers.Baseline(Corpus.Raw) : RealAnswers.Chat(Corpus.DocumentId);

        foreach (var claim in ClaimParser.Parse(answer))
        {
            Assert.Contains(claim.Quote!, claim.Source, StringComparison.Ordinal);
            Assert.Contains(claim.Source, answer.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AQuoteKeepsTheModelsOwnQuotationMarksInsideItEvenWhereTheSourceUsesOthers()
    {
        var claims = ClaimParser.Parse(RealAnswers.Baseline(Corpus.Raw));

        Assert.Equal(
            "Operation objects MUST have exactly one 'op' member, whose value indicates the operation to perform.",
            claims[0].Quote);
        Assert.Equal(Verification.Failed, NormalizedDocument.Create(Corpus.Raw).Resolve(claims[0].Quote!).Verification);
    }

    [Fact]
    public void ANoteAfterTheClosingQuotationMarkIsNotPartOfTheQuote()
    {
        var claims = ClaimParser.Parse(RealAnswers.Baseline(Corpus.Raw));

        Assert.Equal("* **Quote:** \"The target location MUST exist for the operation to be successful.\" (for `remove`)", claims[6].Source);
        Assert.Equal("The target location MUST exist for the operation to be successful.", claims[6].Quote);
    }

    [Fact]
    public void AnElisionTheModelWroteIntoAQuoteIsKeptAndSoTheQuoteIsNotFound()
    {
        var claims = ClaimParser.Parse(RealAnswers.Baseline(Corpus.Raw));

        Assert.Equal("When the operation is applied, the target location MUST reference one of: [...]", claims[4].Quote);
    }

    [Fact]
    public void QuotationMarksInsideAChatQuoteAreKeptAndOnlyTheOuterPairIsRemoved()
    {
        var claims = ClaimParser.Parse(RealAnswers.Chat(Corpus.DocumentId));

        Assert.Equal(
            "Operation objects MUST have exactly one \"op\" member, whose value indicates the operation to perform.",
            claims[0].Quote);
    }

    [Fact]
    public void AnItemWithNoQuoteIsAClaimWithoutAQuoteRatherThanBeingDropped()
    {
        const string Answer = """
            ### Requirement 1
            * **Quote:** "The target location MUST exist."
            * **Test Case:** remove a missing member

            ### Requirement 2
            * **Test Case:** a case with no quote at all
            """;

        var claims = ClaimParser.Parse(Answer);

        Assert.Equal(2, claims.Count);
        Assert.Equal("The target location MUST exist.", claims[0].Quote);
        Assert.Null(claims[1].Quote);
        Assert.Equal("### Requirement 2", claims[1].Item);
    }

    [Fact]
    public void AQuoteGivenAsABlockquoteUnderItsLabelIsReadFromTheBlockquote()
    {
        const string Answer = """
            ### Requirement 1
            **Quote:**
            > "The target location MUST
            > exist."
            """;

        var claim = Assert.Single(ClaimParser.Parse(Answer));

        Assert.Equal("The target location MUST\nexist.", claim.Quote);
    }

    [Fact]
    public void AnUnlabelledBlockquoteIsTheQuoteOfAnItemThatHasNoQuoteField()
    {
        const string Answer = """
            ## 1. The op member
            > Operation objects MUST have exactly one "op" member.

            Test case: omit "op".

            ## 2. The path member
            > Additionally, operation objects MUST have exactly one "path" member.
            """;

        var claims = ClaimParser.Parse(Answer);

        Assert.Equal(["Operation objects MUST have exactly one \"op\" member.", "Additionally, operation objects MUST have exactly one \"path\" member."], claims.Select(claim => claim.Quote));
    }

    [Fact]
    public void NumberedEntriesAtTheStartOfALineAreItemsWhenTheQuotesSitUnderThem()
    {
        const string Answer = """
            Here are the requirements.

            1. **Quote:** "The target location MUST exist."
               - Title: remove a missing member
            2. **Quote:** “The "from" location MUST exist.”
            3. **Requirement:** the path member
            """;

        var claims = ClaimParser.Parse(Answer);

        Assert.Equal(3, claims.Count);
        Assert.Equal("The target location MUST exist.", claims[0].Quote);
        Assert.Equal("The \"from\" location MUST exist.", claims[1].Quote);
        Assert.Null(claims[2].Quote);
    }

    [Fact]
    public void AnOuterHeadingEndsTheItemBeforeIt()
    {
        const string Answer = """
            ## Section 4.1
            ### Requirement 1
            * **Quote:** "The target location MUST exist."
            ## Section 4.2
            * **Quote:** "The from location MUST exist."
            ### Requirement 2
            * **Quote:** "The path MUST be a string."
            """;

        var claims = ClaimParser.Parse(Answer);

        Assert.Equal(3, claims.Count);
        Assert.Equal("### Requirement 1", claims[0].Item);
        Assert.Equal(string.Empty, claims[1].Item);
        Assert.Equal("The from location MUST exist.", claims[1].Quote);
        Assert.Equal("### Requirement 2", claims[2].Item);
    }

    [Fact]
    public void AQuoteFieldOutsideAnyItemIsStillAClaim()
    {
        const string Answer = """
            * **Quote:** "The target location MUST exist."
            * **Quote:** "The from location MUST exist."
            """;

        Assert.Equal(2, ClaimParser.Parse(Answer).Count);
    }

    [Fact]
    public void AnAnswerWithNoItemsAndNoQuotesCannotBeSplitAndFailsAsAWhole()
    {
        const string Answer = """
            The specification defines JSON Patch. Every operation needs an op member, and
            implementations should stop on the first error.
            """;

        Assert.Throws<UnparseableAnswerException>(() => ClaimParser.Parse(Answer));
    }

    [Fact]
    public void AnIntroductionThatMentionsQuotesIsNotMistakenForAQuoteField()
    {
        const string Answer = """
            Below is every requirement together with its exact quote: see each heading.

            ### Requirement 1
            * **Exact Quote:** "The target location MUST exist."
            """;

        var claim = Assert.Single(ClaimParser.Parse(Answer));

        Assert.Equal("The target location MUST exist.", claim.Quote);
    }

    [Theory]
    [InlineData("\"The target MUST exist.\"", "The target MUST exist.")]
    [InlineData("*\"The target MUST exist.\"*", "The target MUST exist.")]
    [InlineData("**The target MUST exist.**", "The target MUST exist.")]
    [InlineData("“The target MUST exist.”", "The target MUST exist.")]
    [InlineData("`The target MUST exist.`", "The target MUST exist.")]
    [InlineData("\"The target **MUST** exist.\"", "The target **MUST** exist.")]
    [InlineData("'op' MUST be present.", "'op' MUST be present.")]
    [InlineData("\"The target MUST exist.", "The target MUST exist.")]
    [InlineData("The target MUST exist.", "The target MUST exist.")]
    public void UnwrappingRemovesOnlyTheMarksAroundTheWholeQuote(string value, string expected)
    {
        Assert.Equal(expected, ClaimParser.Unwrap(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void AnEmptyValueIsNoQuote(string value)
    {
        Assert.Null(ClaimParser.Unwrap(value));
    }

    [Fact]
    public void AQuoteSplitOverCarriageReturnLineEndingsParsesTheSameAsOverLineFeeds()
    {
        const string Answer = "### Requirement 1\n* **Quote:** \"The target location MUST exist.\"\n\n### Requirement 2\n* **Quote:** \"The from location MUST exist.\"\n";

        Assert.Equal(
            ClaimParser.Parse(Answer).Select(claim => claim.Quote),
            ClaimParser.Parse(Answer.Replace("\n", "\r\n", StringComparison.Ordinal)).Select(claim => claim.Quote));
    }
}

internal static class RealAnswers
{
    public static string Baseline(string rawDocument)
    {
        var request = BaselineRun.RequestFor(rawDocument, LlmClientFactory.DefaultModel, PromptFile.Baseline);
        using var entry = JsonDocument.Parse(File.ReadAllText(Repository.PathTo("cache", $"{CacheKey.For(request)}.json")));

        return entry.RootElement.GetProperty("response").GetProperty("text").GetString()!;
    }

    public static string Chat(string documentId)
    {
        var transcript = File.ReadAllText(Repository.PathTo("experiments", "a0", $"{documentId}.md"));
        var closing = transcript.IndexOf("\n---", 3, StringComparison.Ordinal);

        return transcript[(transcript.IndexOf('\n', closing + 1) + 1)..];
    }
}
