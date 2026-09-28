using System.Security.Cryptography;
using System.Text.Json;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public sealed class RunNotAvailableException : Exception
{
    public RunNotAvailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class ReviewRefusedException : Exception
{
    public ReviewRefusedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class RunReadOnlyException : Exception
{
    public RunReadOnlyException(string message)
        : base(message)
    {
    }
}

public sealed record RunListing(
    string Key,
    string DocumentId,
    string RunId,
    RunState State,
    string? Reason,
    DateTimeOffset? StartedAt,
    RunRecord? Record)
{
    public bool IsReference => Key == Workspace.ReferenceKey;

    public bool IsReviewable => State == RunState.Completed;
}

public sealed record LoadedRun(
    string Key,
    string Directory,
    RunContents Contents,
    string DocumentPath,
    string Document,
    string LogPath,
    IReadOnlyList<LoggedDecision> Log,
    ReviewedRun Reviewed,
    VerificationFigures? Figures,
    RunRecord? Record)
{
    public string RunId => Contents.Manifest.RunId;

    public string DocumentId => Contents.Manifest.DocumentId;
}

public sealed class RunCatalog
{
    public const string ReadOnlyReason =
        "The reference run is read-only on this server: it shows the author's own review, and no decision on it is "
        + "accepted here. To try reviewing, start your own run of a corpus document from the run list. Nothing was logged.";

    private readonly Workspace _workspace;
    private readonly RunRecords _records;
    private readonly TimeProvider _timeProvider;

    public RunCatalog(Workspace workspace, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _workspace = workspace;
        _records = new RunRecords(workspace);
        _timeProvider = timeProvider;
    }

    public Workspace Workspace => _workspace;

    public bool ReferenceReadOnly { get; init; }

    public bool IsReadOnly(string key) => ReferenceReadOnly && key == Workspace.ReferenceKey;

    public IReadOnlyList<RunListing> List()
    {
        var listings = new List<RunListing>();

        if (Reference() is { } reference)
        {
            listings.Add(reference);
        }

        listings.AddRange(_records.ReadAll().Select(ListingFor));

        return listings.AsReadOnly();
    }

    public RunListing? Find(string key)
    {
        if (key == Workspace.ReferenceKey)
        {
            return Reference();
        }

        return _records.Read(key) is { } record ? ListingFor(record) : null;
    }

    public async Task<LoadedRun> LoadAsync(string key, CancellationToken cancellationToken)
    {
        var listing = Find(key)
            ?? throw new RunNotAvailableException($"There is no run '{key}'.");

        if (!listing.IsReviewable)
        {
            throw new RunNotAvailableException(
                $"Run {listing.RunId} is {Describe(listing.State)}, so it is not offered for review."
                + (listing.Reason is null ? string.Empty : $" {listing.Reason}"));
        }

        var directory = _workspace.RunDirectory(key);
        RunContents contents;

        try
        {
            contents = await RunReader.ReadAsync(directory, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidRunFilesException exception)
        {
            throw new RunNotAvailableException(exception.Message, exception);
        }

        if (contents.Manifest.RunId != listing.RunId)
        {
            throw new RunNotAvailableException(
                $"'{directory}' holds run {contents.Manifest.RunId}, not run {listing.RunId}.");
        }

        var documentPath = listing.Record is { } record
            ? _workspace.DocumentPath(record)
            : _workspace.CorpusDocument(contents.Manifest.DocumentId);
        var document = await ReadDocumentAsync(documentPath, listing.Record, cancellationToken).ConfigureAwait(false);

        CheckEverySpanHoldsItsQuote(contents.Register, document, documentPath);

        var logPath = _workspace.ReviewLog(key);
        IReadOnlyList<LoggedDecision> log;

        try
        {
            log = await ReviewLog.ReadAsync(logPath, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidReviewLogException exception)
        {
            throw new RunNotAvailableException(
                $"The review log cannot be read, so no reviewed view is shown: {exception.Message}",
                exception);
        }

        var reviewed = ReviewedRun.Build(
            contents.Manifest.RunId,
            contents.Register,
            contents.Cases,
            contents.QueueEntries,
            log);

        return new LoadedRun(
            key,
            directory,
            contents,
            documentPath,
            document,
            logPath,
            log,
            reviewed,
            listing.Record?.Figures ?? await MetricsFiguresAsync(contents.Manifest.RunId, cancellationToken).ConfigureAwait(false),
            listing.Record);
    }

    public async Task RecordCaseDecisionAsync(
        string key,
        string testCaseId,
        CaseDecision decision,
        CaseEdit? edit,
        string author,
        CancellationToken cancellationToken)
    {
        RefuseIfReadOnly(key);

        var run = await LoadAsync(key, cancellationToken).ConfigureAwait(false);
        var reviewed = run.Reviewed.Cases.FirstOrDefault(candidate => candidate.Original.Id == testCaseId)
            ?? throw new ReviewRefusedException($"Run {run.RunId} has no test case '{testCaseId}'. Nothing was logged.");

        if (edit is not null && edit.ChangesNothingIn(reviewed.Current))
        {
            throw new ReviewRefusedException(
                $"The edit of {testCaseId} changes nothing. To keep the case as it is, accept it. Nothing was logged.");
        }

        var record = Construct(() => new ReviewRecord(run.RunId, testCaseId, decision, edit, author, _timeProvider.GetUtcNow()));

        await ReviewLog.AppendAsync(run.LogPath, record, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordQueueDecisionAsync(
        string key,
        string itemId,
        QueueDecision decision,
        string author,
        CancellationToken cancellationToken)
    {
        RefuseIfReadOnly(key);

        var run = await LoadAsync(key, cancellationToken).ConfigureAwait(false);
        var item = run.Reviewed.Items.FirstOrDefault(candidate => candidate.Entry.ItemId == itemId)
            ?? throw new ReviewRefusedException($"Run {run.RunId} has no decision-queue item '{itemId}'. Nothing was logged.");

        var resolution = Construct(() => new QueueResolution(
            run.RunId,
            itemId,
            item.Entry.RequirementId,
            decision,
            author,
            _timeProvider.GetUtcNow()));

        await ReviewLog.AppendAsync(run.LogPath, resolution, cancellationToken).ConfigureAwait(false);
    }

    public static string Describe(RunState state) => state switch
    {
        RunState.Running => "still running",
        RunState.Completed => "completed",
        RunState.Failed => "failed",
        RunState.Cancelled => "cancelled",
        RunState.Interrupted => "interrupted",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private void RefuseIfReadOnly(string key)
    {
        if (IsReadOnly(key))
        {
            throw new RunReadOnlyException(ReadOnlyReason);
        }
    }

    private RunListing? Reference()
    {
        var directory = _workspace.Reference;
        var manifest = Path.Combine(directory, RunArtifacts.ManifestFile);

        if (!File.Exists(manifest))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
        var root = document.RootElement;

        return new RunListing(
            Workspace.ReferenceKey,
            root.GetProperty("document_id").GetString()!,
            root.GetProperty("run_id").GetString()!,
            RunReader.IsComplete(directory) ? RunState.Completed : RunState.Failed,
            RunReader.IsComplete(directory) ? null : $"'{directory}' is missing some of the run's files.",
            null,
            null);
    }

    private static RunListing ListingFor(RunRecord record) => new(
        record.RunId,
        record.DocumentId,
        record.RunId,
        record.State,
        record.Reason,
        record.StartedAt,
        record);

    private static async Task<string> ReadDocumentAsync(string path, RunRecord? record, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new RunNotAvailableException(
                $"The run's source document '{path}' is missing, so its quotes cannot be shown in context.");
        }

        if (record is not null)
        {
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)));

            if (sha256 != record.DocumentSha256)
            {
                throw new RunNotAvailableException(
                    $"'{path}' has changed since the run: its SHA-256 is {sha256}, the run recorded {record.DocumentSha256}.");
            }
        }

        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static void CheckEverySpanHoldsItsQuote(IReadOnlyList<Requirement> register, string document, string path)
    {
        foreach (var requirement in register)
        {
            var span = requirement.Span;

            if (span.Start < 0 || span.End > document.Length
                || TextNormalizer.Normalize(document[span.Start..span.End]) != TextNormalizer.Normalize(requirement.Text))
            {
                throw new RunNotAvailableException(
                    $"'{path}' does not hold requirement {requirement.Id}'s quote at its span [{span.Start}, {span.End}). "
                    + "The document is not the one this run was made from, so review is not offered.");
            }
        }
    }

    private async Task<VerificationFigures?> MetricsFiguresAsync(string runId, CancellationToken cancellationToken)
    {
        if (!Workspace.IsRunId(runId))
        {
            return null;
        }

        var path = _workspace.MetricsFile(runId);

        if (!File.Exists(path))
        {
            return null;
        }

        using var metrics = JsonDocument.Parse(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));

        if (!metrics.RootElement.TryGetProperty("claimed", out var claimed))
        {
            return null;
        }

        return new VerificationFigures(
            claimed.GetProperty("claims").GetInt32(),
            claimed.GetProperty("quotes_found_once").GetInt32(),
            claimed.GetProperty("quotes_found_more_than_once").GetInt32(),
            claimed.GetProperty("quotes_not_found").GetInt32());
    }

    private static T Construct<T>(Func<T> construct)
    {
        try
        {
            return construct();
        }
        catch (ArgumentException exception)
        {
            var message = exception.ParamName is null
                ? exception.Message
                : exception.Message.Replace($" (Parameter '{exception.ParamName}')", string.Empty, StringComparison.Ordinal);

            throw new ReviewRefusedException($"{message} Nothing was logged.", exception);
        }
    }
}
