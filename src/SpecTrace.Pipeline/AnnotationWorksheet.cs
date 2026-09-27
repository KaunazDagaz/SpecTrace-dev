using System.Globalization;
using System.Text;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public static class AnnotationWorksheet
{
    private static readonly UTF8Encoding Utf8WithoutMark = new(encoderShouldEmitUTF8Identifier: false);

    public static string Render(string documentId, IReadOnlyList<KeywordSentence> sentences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(sentences);

        var yaml = new StringBuilder();

        Line(yaml, $"document: {documentId}");
        Line(yaml, "annotation_rules_commit:");
        Line(yaml, "annotator:");
        Line(yaml, "annotated_at:");
        Line(yaml, "candidates:");

        foreach (var sentence in sentences)
        {
            Line(yaml, $"- candidate: {sentence.Number.ToString(CultureInfo.InvariantCulture)}");
            Line(yaml, $"  section: \"{sentence.Section}\"");
            Line(yaml, $"  lines: {Lines(sentence)}");
            Line(yaml, $"  keywords: [{string.Join(", ", sentence.Keywords)}]");
            Line(yaml, "  source: |-");

            foreach (var source in sentence.SourceLines)
            {
                Line(yaml, $"    {source}");
            }

            Line(yaml, "  sentence: >-");
            Line(yaml, $"    {sentence.Sentence}");
            Line(yaml, "  decision:");
            Line(yaml, "  obligations:");
            Line(yaml, "  - quote: >-");
            Line(yaml, "    modality:");
            Line(yaml, "    testability:");
            Line(yaml, "    section:");
        }

        return yaml.ToString();
    }

    public static async Task<int> WriteAsync(string documentPath, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (File.Exists(outputPath))
        {
            throw new InvalidOperationException(
                $"{outputPath} already exists. A worksheet is never written over an existing file, so a filled one "
                + "cannot be lost; delete it first to generate it again.");
        }

        var raw = await File.ReadAllTextAsync(documentPath, cancellationToken).ConfigureAwait(false);
        var sentences = KeywordSentences.Find(raw);
        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            outputPath,
            Render(ExtractionRun.DocumentIdFor(documentPath), sentences),
            Utf8WithoutMark,
            cancellationToken).ConfigureAwait(false);

        return sentences.Count;
    }

    private static string Lines(KeywordSentence sentence) =>
        sentence.FirstLine == sentence.LastLine
            ? sentence.FirstLine.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{sentence.FirstLine}-{sentence.LastLine}");

    private static void Line(StringBuilder yaml, string line) => yaml.Append(line).Append('\n');
}
