using System.Text.RegularExpressions;

namespace SpecTrace.Pipeline;

public sealed partial record Workspace(string Corpus, string Cache, string Runs, string Reference, string Experiments)
{
    public const string ReferenceKey = "reference";

    public const string WebDirectoryName = "web";

    public static Workspace Default { get; } = Under(".");

    public string WebRoot => Path.Combine(Runs, WebDirectoryName);

    public string StatusDirectory => Path.Combine(WebRoot, "status");

    public string UploadsDirectory => Path.Combine(WebRoot, "uploads");

    public string UploadCache => Path.Combine(WebRoot, "cache");

    public string ReviewsDirectory => Path.Combine(WebRoot, "reviews");

    public static Workspace Under(string root) => new(
        Path.Combine(root, "corpus"),
        Path.Combine(root, "cache"),
        Path.Combine(root, "runs"),
        Path.Combine(root, "runs", ReferenceKey),
        Path.Combine(root, "experiments"));

    public static bool IsRunId(string value) => RunIdPattern().IsMatch(value);

    public static bool IsRunKey(string value) => value == ReferenceKey || IsRunId(value);

    public string RunDirectory(string key) =>
        key == ReferenceKey
            ? Reference
            : IsRunId(key)
                ? Path.Combine(Runs, key)
                : throw new ArgumentException($"'{key}' is not a run key.", nameof(key));

    public string ReviewLog(string key) =>
        IsRunKey(key)
            ? Path.Combine(ReviewsDirectory, $"{key}.jsonl")
            : throw new ArgumentException($"'{key}' is not a run key.", nameof(key));

    public string CorpusDocument(string documentId) =>
        Path.Combine(Corpus, $"{PlainName(documentId)}.txt");

    public string UploadedDocument(string sha256, string documentId) =>
        Sha256Pattern().IsMatch(sha256)
            ? Path.Combine(UploadsDirectory, sha256[..12], $"{PlainName(documentId)}.txt")
            : throw new ArgumentException($"'{sha256}' is not a SHA-256 in hex.", nameof(sha256));

    public string DocumentPath(RunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.Source switch
        {
            DocumentSource.Corpus => CorpusDocument(record.DocumentId),
            DocumentSource.Upload => UploadedDocument(record.DocumentSha256, record.DocumentId),
            _ => throw new ArgumentOutOfRangeException(nameof(record), record.Source, null),
        };
    }

    public string MetricsFile(string runId) =>
        IsRunId(runId)
            ? Path.Combine(Experiments, $"{runId}.metrics.json")
            : throw new ArgumentException($"'{runId}' is not a run ID.", nameof(runId));

    private static string PlainName(string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        if (documentId != Path.GetFileName(documentId) || documentId is "." or ".." || documentId.Contains('\\'))
        {
            throw new ArgumentException($"'{documentId}' is not a plain document name.", nameof(documentId));
        }

        return documentId;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex RunIdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
