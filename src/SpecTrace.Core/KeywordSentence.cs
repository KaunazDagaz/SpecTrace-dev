namespace SpecTrace.Core;

public sealed record KeywordSentence
{
    public KeywordSentence(
        int number,
        string section,
        int firstLine,
        int lastLine,
        IReadOnlyList<string> keywords,
        string sentence,
        IReadOnlyList<string> sourceLines,
        TextSpan span)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentOutOfRangeException.ThrowIfLessThan(firstLine, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(lastLine, firstLine);
        ArgumentNullException.ThrowIfNull(keywords);
        ArgumentException.ThrowIfNullOrWhiteSpace(sentence);
        ArgumentNullException.ThrowIfNull(sourceLines);

        if (keywords.Count == 0)
        {
            throw new ArgumentException("A keyword sentence carries at least one keyword.", nameof(keywords));
        }

        Number = number;
        Section = section;
        FirstLine = firstLine;
        LastLine = lastLine;
        Keywords = [.. keywords];
        Sentence = sentence;
        SourceLines = [.. sourceLines];
        Span = span;
    }

    public int Number { get; }

    public string Section { get; }

    public int FirstLine { get; }

    public int LastLine { get; }

    public IReadOnlyList<string> Keywords { get; }

    public string Sentence { get; }

    public IReadOnlyList<string> SourceLines { get; }

    public TextSpan Span { get; }
}
