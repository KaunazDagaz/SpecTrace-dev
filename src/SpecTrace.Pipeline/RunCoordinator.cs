using SpecTrace.Llm;

namespace SpecTrace.Pipeline;

public abstract record StartOutcome
{
    public sealed record Refused(string Reason) : StartOutcome;

    public sealed record Busy(RunRecord? Current) : StartOutcome;

    public sealed record AlreadyRun(string RunId) : StartOutcome;

    public sealed record Started(string RunId, Task Completion) : StartOutcome;
}

public sealed class RunCoordinator
{
    private readonly Workspace _workspace;
    private readonly RunRecords _records;
    private readonly ModelClientFactory _modelClients;
    private readonly PromptSet _prompts;
    private readonly string _model;
    private readonly bool _offline;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationToken _stopping;
    private readonly SemaphoreSlim _slot = new(1, 1);
    private Task _current = Task.CompletedTask;
    private RunRecord? _running;

    public RunCoordinator(
        Workspace workspace,
        ModelClientFactory modelClients,
        PromptSet prompts,
        string model,
        bool offline,
        TimeProvider timeProvider,
        CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(modelClients);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _workspace = workspace;
        _records = new RunRecords(workspace);
        _modelClients = modelClients;
        _prompts = prompts;
        _model = model;
        _offline = offline;
        _timeProvider = timeProvider;
        _stopping = stopping;
    }

    public bool Offline => _offline;

    public Task Current => _current;

    public RunRecord? Running => _running;

    public int RecoverInterrupted()
    {
        var stale = _records.ReadAll().Where(record => record.State == RunState.Running).ToList();

        foreach (var record in stale)
        {
            _records.Write(record with
            {
                State = RunState.Interrupted,
                Reason = "The server stopped before this run finished: it was restarted, or it crashed. "
                    + "Nothing of the run is offered for review. Upload the document again to run it again.",
                FinishedAt = _timeProvider.GetUtcNow(),
            });
        }

        return stale.Count;
    }

    public async Task<StartOutcome> StartAsync(string? fileName, byte[] bytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            DocumentIntake.Validate(bytes);
        }
        catch (UploadRejectedException exception)
        {
            return new StartOutcome.Refused(exception.Message);
        }

        if (!_slot.Wait(0))
        {
            return new StartOutcome.Busy(_running);
        }

        var releaseSlot = true;

        try
        {
            var document = await DocumentIntake.PrepareAsync(_workspace, fileName, bytes, cancellationToken).ConfigureAwait(false);
            var runId = PipelineRun.RunIdFor(document.DocumentId, document.Raw, _model, _prompts);

            if (!Workspace.IsRunId(runId))
            {
                return new StartOutcome.Refused(
                    $"The corpus file name '{document.DocumentId}' cannot name a run: use lower-case letters, digits and hyphens.");
            }

            if (_records.Read(runId) is { State: RunState.Completed }
                && RunReader.IsComplete(_workspace.RunDirectory(runId)))
            {
                return new StartOutcome.AlreadyRun(runId);
            }

            var record = new RunRecord(
                runId,
                document.DocumentId,
                document.Source,
                document.Sha256,
                _offline,
                RunState.Running,
                Reason: null,
                _timeProvider.GetUtcNow(),
                FinishedAt: null,
                Figures: null);

            _records.Write(record);
            _running = record;
            releaseSlot = false;
            _current = Task.Run(() => ExecuteAsync(document, record), CancellationToken.None);

            return new StartOutcome.Started(runId, _current);
        }
        catch (UploadRejectedException exception)
        {
            return new StartOutcome.Refused(exception.Message);
        }
        finally
        {
            if (releaseSlot)
            {
                _slot.Release();
            }
        }
    }

    public static string Describe(Exception exception, bool offline)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            OfflineCacheMissException miss =>
                "This document is not in the cache"
                + (offline ? $", and the server runs offline ({PipelineLaunch.OfflineVariable}=1), so nothing was sent to the model. " : ". ")
                + "Only a document whose model calls are recorded in the committed cache can run offline; a live run needs "
                + $"{PipelineLaunch.ApiKeyVariable} in the server's environment. The first missing call: {miss.Call ?? "the first request"}.",
            LlmQuotaExhaustedException quota =>
                $"The model provider's daily request quota is exhausted (quota {quota.QuotaId}"
                + (quota.Limit is null ? string.Empty : $", limit {quota.Limit}")
                + "). The run stopped. Responses already received stay in the cache, so a run of the same document "
                + "after the quota resets resumes from them.",
            LlmException llm => llm.Message,
            InvalidOperationException invalid => invalid.Message,
            _ => $"{exception.GetType().Name}: {exception.Message}",
        };
    }

    private async Task ExecuteAsync(PreparedDocument document, RunRecord record)
    {
        try
        {
            var client = _modelClients(document.CacheDirectory, _offline);
            var (result, _) = await PipelineLaunch
                .RunAsync(document.Path, client, _model, _prompts, _timeProvider, _workspace.RunDirectory(record.RunId), _stopping)
                .ConfigureAwait(false);

            if (result.RunId != record.RunId)
            {
                throw new InvalidOperationException(
                    $"The pipeline produced run {result.RunId}, not the expected run {record.RunId}; the run is not offered for review.");
            }

            var outcome = result.Extraction.Outcome;

            Finish(record with
            {
                State = RunState.Completed,
                Figures = new VerificationFigures(
                    outcome.ClaimCount,
                    outcome.ExactClaimCount,
                    outcome.AmbiguousClaimCount,
                    outcome.Rejected.Count),
            });
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            Finish(record with
            {
                State = RunState.Cancelled,
                Reason = "The server was stopped while this run was in progress. Nothing of the run is offered for review.",
            });
        }
        catch (Exception exception)
        {
            Finish(record with { State = RunState.Failed, Reason = Describe(exception, _offline) });
        }
        finally
        {
            _running = null;
            _slot.Release();
        }
    }

    private void Finish(RunRecord record) =>
        _records.Write(record with { FinishedAt = _timeProvider.GetUtcNow() });
}
