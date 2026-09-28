using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpecTrace.Pipeline;

public enum RunState
{
    Running,
    Completed,
    Failed,
    Cancelled,
    Interrupted,
}

public enum DocumentSource
{
    Corpus,
    Upload,
}

public sealed record VerificationFigures(int Claims, int FoundOnce, int FoundMoreThanOnce, int NotFound)
{
    public double? VerificationRate => Claims == 0 ? null : (double)FoundOnce / Claims;
}

public sealed record RunRecord(
    string RunId,
    string DocumentId,
    DocumentSource Source,
    string DocumentSha256,
    bool Offline,
    RunState State,
    string? Reason,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    VerificationFigures? Figures)
{
    public bool IsFinished => State != RunState.Running;
}

public sealed class RunRecords
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    private readonly string _directory;

    public RunRecords(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        _directory = workspace.StatusDirectory;
    }

    public RunRecord? Read(string runId)
    {
        if (!Workspace.IsRunId(runId))
        {
            return null;
        }

        var path = PathFor(runId);

        return File.Exists(path) ? Deserialize(path) : null;
    }

    public IReadOnlyList<RunRecord> ReadAll()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_directory, "*.json")
            .Where(path => Workspace.IsRunId(Path.GetFileNameWithoutExtension(path)))
            .Select(Deserialize)
            .OrderByDescending(record => record.StartedAt)
            .ThenBy(record => record.RunId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
    }

    public void Write(RunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!Workspace.IsRunId(record.RunId))
        {
            throw new ArgumentException($"'{record.RunId}' is not a run ID.", nameof(record));
        }

        Directory.CreateDirectory(_directory);

        var path = PathFor(record.RunId);
        var temporary = $"{path}.{Environment.ProcessId}.tmp";

        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(record, Options));
        File.Move(temporary, path, overwrite: true);
    }

    private string PathFor(string runId) => Path.Combine(_directory, $"{runId}.json");

    private static RunRecord Deserialize(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<RunRecord>(File.ReadAllBytes(path), Options)
                ?? throw new InvalidRunFilesException($"'{path}' holds null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidRunFilesException($"'{path}' is not a readable run record: {exception.Message}", exception);
        }
    }
}
