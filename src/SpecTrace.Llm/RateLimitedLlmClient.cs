namespace SpecTrace.Llm;

public sealed class RateLimitedLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly TimeSpan _minimumInterval;
    private readonly int _maximumAttempts;
    private readonly TimeSpan _firstBackoff;
    private readonly TimeSpan _maximumBackoff;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private long _lastRequestTimestamp;
    private bool _hasRequested;

    public RateLimitedLlmClient(
        ILlmClient inner,
        int requestsPerMinute = 10,
        int maximumAttempts = 6,
        TimeSpan? firstBackoff = null,
        TimeSpan? maximumBackoff = null,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestsPerMinute);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAttempts);

        _inner = inner;
        _minimumInterval = TimeSpan.FromSeconds(60.0 / requestsPerMinute);
        _maximumAttempts = maximumAttempts;
        _firstBackoff = firstBackoff ?? TimeSpan.FromSeconds(2);
        _maximumBackoff = maximumBackoff ?? TimeSpan.FromSeconds(32);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _delayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, token));
    }

    public int ProviderCalls { get; private set; }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var backoff = _firstBackoff;

            for (var attempt = 1; ; attempt++)
            {
                await WaitForSlotAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    ProviderCalls++;

                    return await _inner.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (LlmRetryableException retryable) when (attempt < _maximumAttempts)
                {
                    await DelayAsync(retryable.RetryAfter ?? backoff, cancellationToken)
                        .ConfigureAwait(false);

                    backoff = backoff >= _maximumBackoff ? _maximumBackoff : backoff * 2;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        if (_hasRequested)
        {
            var elapsed = _timeProvider.GetElapsedTime(_lastRequestTimestamp);
            var remaining = _minimumInterval - elapsed;

            if (remaining > TimeSpan.Zero)
            {
                await DelayAsync(remaining, cancellationToken).ConfigureAwait(false);
            }
        }

        _lastRequestTimestamp = _timeProvider.GetTimestamp();
        _hasRequested = true;
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        _delayAsync(delay, cancellationToken);
}
