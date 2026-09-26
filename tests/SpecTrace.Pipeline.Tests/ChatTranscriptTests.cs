namespace SpecTrace.Pipeline.Tests;

public sealed class ChatTranscriptTests
{
    private static readonly string Complete = """
        ---
        document: rfc6902.txt
        captured_at: 2026-09-26T13:05+03:00
        interface: gemini.google.com
        model: Gemini 3.5 Flash
        mode: default, no Gems, no Deep Research
        input: file attachment
        share_link: https://g.co/gemini/share/abc123
        prompt: |
          List every requirement in the following specification. For each
          one, quote the exact sentence it comes from, then write test cases
          for it with a title, input and expected result.
        ---

        ### 1. The op member
        * **Exact Quote:** "Operation objects MUST have exactly one "op" member, whose value indicates the operation to perform."
        """.ReplaceLineEndings("\n");

    [Fact]
    public void ACompleteTranscriptYieldsItsFieldsAndTheAnswerUnchanged()
    {
        var transcript = ChatTranscript.Parse(Complete, "rfc6902.txt", PromptFile.Baseline);

        Assert.Equal("rfc6902.txt", transcript.Document);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 13, 5, 0, TimeSpan.FromHours(3)), transcript.CapturedAt);
        Assert.Equal("gemini.google.com", transcript.Interface);
        Assert.Equal("Gemini 3.5 Flash", transcript.Model);
        Assert.Equal("file attachment", transcript.Input);
        Assert.Equal("https://g.co/gemini/share/abc123", transcript.ShareLink);
        Assert.Equal(Complete[(Complete.IndexOf("\n---\n", StringComparison.Ordinal) + 5)..], transcript.Answer);
    }

    [Fact]
    public void ATranscriptSavedWithCarriageReturnLineEndingsReadsTheSame()
    {
        var crlf = Complete.Replace("\n", "\r\n", StringComparison.Ordinal);

        var transcript = ChatTranscript.Parse(crlf, "rfc6902.txt", PromptFile.Baseline);

        Assert.Equal("Gemini 3.5 Flash", transcript.Model);
        Assert.StartsWith("\r\n### 1. The op member", transcript.Answer, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceholdersLeftInTheFrontMatterAreNamedAndTheTranscriptIsNotScored()
    {
        var draft = Complete
            .Replace("model: Gemini 3.5 Flash", "model: <model name as shown in the interface>", StringComparison.Ordinal)
            .Replace("share_link: https://g.co/gemini/share/abc123", "share_link: <public share link to the chat>", StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidTranscriptException>(
            () => ChatTranscript.Parse(draft, "rfc6902.txt", PromptFile.Baseline));

        Assert.Equal(
            [
                "'model' is still the placeholder <model name as shown in the interface>",
                "'share_link' is still the placeholder <public share link to the chat>",
            ],
            exception.Problems);
    }

    [Fact]
    public void APromptOtherThanTheBaselinePromptIsRefusedSoTheArmsAreAskedTheSameThing()
    {
        var other = Complete.Replace("then write test cases", "then write two test cases", StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidTranscriptException>(
            () => ChatTranscript.Parse(other, "rfc6902.txt", PromptFile.Baseline));

        Assert.Contains(exception.Problems, problem => problem.StartsWith("'prompt' is not the baseline prompt", StringComparison.Ordinal));
    }

    [Fact]
    public void ATranscriptOfAnotherDocumentIsRefused()
    {
        var exception = Assert.Throws<InvalidTranscriptException>(
            () => ChatTranscript.Parse(Complete, "rfc10031.txt", PromptFile.Baseline));

        Assert.Contains("'document' is 'rfc6902.txt', but the document being scored is 'rfc10031.txt'", exception.Problems);
    }

    [Theory]
    [InlineData("captured_at: 2026-09-26T13:05+03:00", "captured_at: 2026-09-26 13:05", "'captured_at' is '2026-09-26 13:05', not a date and time with a UTC offset")]
    [InlineData("share_link: https://g.co/gemini/share/abc123", "share_link: g.co/gemini/share/abc123", "'share_link' is 'g.co/gemini/share/abc123', not an https link")]
    [InlineData("interface: gemini.google.com\n", "", "'interface' is missing")]
    public void AFieldThatIsMissingOrMalformedIsNamed(string field, string replacement, string problem)
    {
        var broken = Complete.Replace(field, replacement, StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidTranscriptException>(
            () => ChatTranscript.Parse(broken, "rfc6902.txt", PromptFile.Baseline));

        Assert.Contains(problem, exception.Problems);
    }

    [Fact]
    public void AFileWithoutFrontMatterIsRefusedAsAWhole()
    {
        var exception = Assert.Throws<InvalidTranscriptException>(
            () => ChatTranscript.Parse("### 1. The op member\n* **Quote:** \"x\"\n", "rfc6902.txt", PromptFile.Baseline));

        Assert.Contains("front matter", exception.Message, StringComparison.Ordinal);
    }
}
