using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public sealed class InvalidReviewLogException : Exception
{
    public InvalidReviewLogException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public static class ReviewLog
{
    public const string TestCaseTarget = "test_case";

    public const string QueueItemTarget = "decision_queue_item";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    private static readonly string[] CaseFields =
        ["run_id", "target", "test_case_id", "decision", "author", "author_self_declared", "at"];

    private static readonly string[] EditFields =
        ["run_id", "target", "test_case_id", "decision", "edited_case", "author", "author_self_declared", "at"];

    private static readonly string[] ItemFields =
        ["run_id", "target", "item_id", "requirement_id", "decision", "author", "author_self_declared", "at"];

    private static readonly string[] EditedCaseFields =
        ["title", "type", "precondition", "input", "expected_result"];

    public static string Spell(CaseDecision decision) => decision switch
    {
        CaseDecision.Accept => "accept",
        CaseDecision.Edit => "edit",
        CaseDecision.Reject => "reject",
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, null),
    };

    public static string Spell(QueueDecision decision) => decision switch
    {
        QueueDecision.Testable => "testable",
        QueueDecision.NotTestable => "not_testable",
        QueueDecision.Defer => "defer",
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, null),
    };

    public static async Task AppendAsync(string path, LoggedDecision decision, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(decision);

        var line = Serialize(decision);

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            EnsureLastLineIsComplete(path);

            using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 0,
                FileOptions.WriteThrough);

            stream.Write(line);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<IReadOnlyList<LoggedDecision>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] bytes;

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }

        return Parse(path, bytes);
    }

    public static IReadOnlyList<LoggedDecision> Parse(string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        string text;

        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidReviewLogException($"{path} is not valid UTF-8.", exception);
        }

        if (text.Length == 0)
        {
            return [];
        }

        var lines = text.Split('\n');

        if (lines[^1].Length != 0)
        {
            throw new InvalidReviewLogException($"{path}, line {lines.Length}: the line is incomplete; it has no line end.");
        }

        var decisions = new List<LoggedDecision>(lines.Length - 1);

        for (var index = 0; index < lines.Length - 1; index++)
        {
            decisions.Add(ParseLine(path, lines[index], index + 1));
        }

        return decisions.AsReadOnly();
    }

    public static byte[] Serialize(LoggedDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("run_id", decision.RunId);

            switch (decision)
            {
                case ReviewRecord record:
                    writer.WriteString("target", TestCaseTarget);
                    writer.WriteString("test_case_id", record.TestCaseId);
                    writer.WriteString("decision", Spell(record.Decision));

                    if (record.Edit is { } edit)
                    {
                        writer.WriteStartObject("edited_case");
                        writer.WriteString("title", edit.Title);
                        writer.WriteString("type", RunArtifacts.Spell(edit.Type));
                        writer.WriteString("precondition", edit.Precondition);
                        writer.WriteString("input", edit.Input);
                        writer.WriteString("expected_result", edit.ExpectedResult);
                        writer.WriteEndObject();
                    }

                    break;

                case QueueResolution resolution:
                    writer.WriteString("target", QueueItemTarget);
                    writer.WriteString("item_id", resolution.ItemId);

                    if (resolution.RequirementId is null)
                    {
                        writer.WriteNull("requirement_id");
                    }
                    else
                    {
                        writer.WriteString("requirement_id", resolution.RequirementId);
                    }

                    writer.WriteString("decision", Spell(resolution.Decision));
                    break;

                default:
                    throw new ArgumentException("Unknown kind of decision.", nameof(decision));
            }

            writer.WriteString("author", decision.Author);
            writer.WriteBoolean("author_self_declared", true);
            writer.WriteString("at", decision.At.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');

        return buffer.ToArray();
    }

    private static void EnsureLastLineIsComplete(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (stream.Length == 0)
        {
            return;
        }

        stream.Seek(-1, SeekOrigin.End);

        if (stream.ReadByte() != '\n')
        {
            throw new InvalidReviewLogException(
                $"{path} ends in an incomplete line, so nothing was appended. Earlier lines are never changed; "
                + "inspect the file by hand.");
        }
    }

    private static LoggedDecision ParseLine(string path, string line, int number)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("the line is not a JSON object");
            }

            var target = RequiredString(root, "target");
            var runId = RequiredString(root, "run_id");
            var author = RequiredString(root, "author");

            if (!root.TryGetProperty("author_self_declared", out var selfDeclared) || selfDeclared.ValueKind != JsonValueKind.True)
            {
                throw new FormatException("'author_self_declared' must be true");
            }

            var at = DateTimeOffset.Parse(
                RequiredString(root, "at"),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind);

            switch (target)
            {
                case TestCaseTarget:
                    var decision = Spelled.Parse<CaseDecision>(RequiredString(root, "decision"), Spell);
                    ExpectFields(root, decision == CaseDecision.Edit ? EditFields : CaseFields);

                    return new ReviewRecord(
                        runId,
                        RequiredString(root, "test_case_id"),
                        decision,
                        decision == CaseDecision.Edit ? EditedCase(root.GetProperty("edited_case")) : null,
                        author,
                        at);

                case QueueItemTarget:
                    ExpectFields(root, ItemFields);

                    return new QueueResolution(
                        runId,
                        RequiredString(root, "item_id"),
                        OptionalString(root, "requirement_id"),
                        Spelled.Parse<QueueDecision>(RequiredString(root, "decision"), Spell),
                        author,
                        at);

                default:
                    throw new FormatException($"'{target}' is not a review target");
            }
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidReviewLogException($"{path}, line {number}: {exception.Message}", exception);
        }
    }

    private static CaseEdit EditedCase(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("'edited_case' must be an object");
        }

        ExpectFields(element, EditedCaseFields);

        return new CaseEdit(
            RequiredString(element, "title"),
            Spelled.Parse<CaseType>(RequiredString(element, "type"), RunArtifacts.Spell),
            RequiredString(element, "precondition"),
            RequiredString(element, "input"),
            RequiredString(element, "expected_result"));
    }

    private static void ExpectFields(JsonElement element, string[] expected)
    {
        var actual = element.EnumerateObject().Select(property => property.Name).ToList();

        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new FormatException(
                $"expected the fields {string.Join(", ", expected)} in that order; found {string.Join(", ", actual)}");
        }
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new FormatException($"'{name}' must be a string");

    private static string? OptionalString(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetProperty(name).GetString(),
            _ => throw new FormatException($"'{name}' must be a string or null"),
        };
}
