using System.Globalization;
using System.Text.RegularExpressions;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public sealed partial record ChatTranscript(
    string Document,
    DateTimeOffset CapturedAt,
    string Interface,
    string Model,
    string Mode,
    string Input,
    string ShareLink,
    string Prompt,
    string Answer)
{
    public const string Delimiter = "---";

    public static readonly IReadOnlyList<string> Fields =
        ["document", "captured_at", "interface", "model", "mode", "input", "share_link", "prompt"];

    public static string PathFor(string transcriptDirectory, string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcriptDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        return System.IO.Path.Combine(transcriptDirectory, $"{documentId}.md");
    }

    public static ChatTranscript Parse(string content, string documentFileName, PromptFile prompt)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentFileName);
        ArgumentNullException.ThrowIfNull(prompt);

        var (frontMatter, answer) = Split(content);
        var values = ReadFields(frontMatter);
        var problems = new List<string>();

        foreach (var field in Fields)
        {
            if (!values.TryGetValue(field, out var value) || value.Trim().Length == 0)
            {
                problems.Add($"'{field}' is missing");
            }
            else if (PlaceholderPattern().IsMatch(value.Trim()))
            {
                problems.Add($"'{field}' is still the placeholder {value.Trim()}");
            }
        }

        var capturedAt = DateTimeOffset.MinValue;

        if (values.TryGetValue("captured_at", out var captured)
            && !PlaceholderPattern().IsMatch(captured.Trim())
            && !(OffsetPattern().IsMatch(captured.Trim())
                && DateTimeOffset.TryParse(captured.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out capturedAt)))
        {
            problems.Add($"'captured_at' is '{captured.Trim()}', not a date and time with a UTC offset");
        }

        if (values.TryGetValue("document", out var document)
            && !PlaceholderPattern().IsMatch(document.Trim())
            && !string.Equals(document.Trim(), documentFileName, StringComparison.Ordinal))
        {
            problems.Add($"'document' is '{document.Trim()}', but the document being scored is '{documentFileName}'");
        }

        if (values.TryGetValue("share_link", out var link)
            && !PlaceholderPattern().IsMatch(link.Trim())
            && !(Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
        {
            problems.Add($"'share_link' is '{link.Trim()}', not an https link");
        }

        if (values.TryGetValue("prompt", out var sent)
            && !string.Equals(TextNormalizer.Normalize(sent), TextNormalizer.Normalize(prompt.Text), StringComparison.Ordinal))
        {
            problems.Add($"'prompt' is not the baseline prompt in {prompt.Name}, so the chat arm and the baseline were not asked the same thing");
        }

        if (answer.Trim().Length == 0)
        {
            problems.Add("the answer after the front matter is empty");
        }

        if (problems.Count > 0)
        {
            throw new InvalidTranscriptException(problems);
        }

        return new ChatTranscript(
            values["document"].Trim(),
            capturedAt,
            values["interface"].Trim(),
            values["model"].Trim(),
            values["mode"].Trim(),
            values["input"].Trim(),
            values["share_link"].Trim(),
            values["prompt"],
            answer);
    }

    private static (List<string> FrontMatter, string Answer) Split(string content)
    {
        var position = content.StartsWith('﻿') ? 1 : 0;
        var frontMatter = new List<string>();
        var opened = false;

        while (position < content.Length)
        {
            var end = content.IndexOf('\n', position);
            var next = end < 0 ? content.Length : end + 1;
            var line = content[position..(end < 0 ? content.Length : end)].TrimEnd('\r');

            if (!opened)
            {
                if (line != Delimiter)
                {
                    break;
                }

                opened = true;
            }
            else if (line == Delimiter)
            {
                return (frontMatter, content[next..]);
            }
            else
            {
                frontMatter.Add(line);
            }

            position = next;
        }

        throw new InvalidTranscriptException(
            [$"the file does not start with front matter between two '{Delimiter}' lines"]);
    }

    private static Dictionary<string, string> ReadFields(List<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < lines.Count; i++)
        {
            var match = FieldPattern().Match(lines[i]);

            if (!match.Success)
            {
                continue;
            }

            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value.Trim();

            if (value is "|" or "|-" or ">" or ">-")
            {
                var block = new List<string>();

                while (i + 1 < lines.Count && (lines[i + 1].Length == 0 || char.IsWhiteSpace(lines[i + 1][0])))
                {
                    block.Add(lines[++i].Trim());
                }

                value = string.Join('\n', block).Trim();
            }
            else if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return values;
    }

    [GeneratedRegex("^(?<key>[a-z_]+):(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldPattern();

    [GeneratedRegex("^<.*>$", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex("(Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex OffsetPattern();
}

public sealed class InvalidTranscriptException : Exception
{
    public InvalidTranscriptException(IReadOnlyList<string> problems)
        : base($"The transcript cannot be scored: {string.Join("; ", problems)}.")
    {
        Problems = problems;
    }

    public IReadOnlyList<string> Problems { get; }
}
