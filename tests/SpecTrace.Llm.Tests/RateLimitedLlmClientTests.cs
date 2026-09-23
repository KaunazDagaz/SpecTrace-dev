namespace SpecTrace.Llm.Tests;

public sealed class RateLimitedLlmClientTests
{
    private static readonly LlmRequest AnyRequest =
        new("system", "user", "gemini-3.5-flash", 0, 1024, null, "hash");

    private sealed class FailingLlmClient : ILlmClient
    {
        private readonly Queue<Exception> _failures;

        public FailingLlmClient(params Exception[] failures) => _failures = new Queue<Exception>(failures);

        public int Calls { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            Calls++;

            return _failures.Count > 0
                ? Task.FromException<LlmResponse>(_failures.Dequeue())
                : Task.FromResult(new LlmResponse("recovered", 1, 1, FromCache: false));
        }
    }

    private static RateLimitedLlmClient Unspaced(
        ILlmClient inner,
        DelayRecorder recorder,
        int maximumAttempts = 5) =>
        new(
            inner,
            requestsPerMinute: 60_000,
            maximumAttempts: maximumAttempts,
            firstBackoff: TimeSpan.FromSeconds(1),
            timeProvider: recorder.Clock,
            delayAsync: recorder.RecordAsync);

    private static DelayRecorder NewRecorder() => new();

    [Fact]
    public async Task ARateLimitedCallIsRetriedAfterAnExponentiallyGrowingWait()
    {
        var recorder = NewRecorder();
        var inner = new FailingLlmClient(
            new LlmRateLimitException("429", retryAfter: null),
            new LlmRateLimitException("429", retryAfter: null),
            new LlmRateLimitException("429", retryAfter: null));
        var client = Unspaced(inner, recorder);

        var response = await client.CompleteAsync(AnyRequest, CancellationToken.None);

        Assert.Equal("recovered", response.Text);
        Assert.Equal(4, inner.Calls);
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)],
            recorder.Delays);
    }

    [Fact]
    public async Task ATemporarilyUnavailableProviderIsRetriedOnTheSameSchedule()
    {
        var recorder = NewRecorder();
        var inner = new FailingLlmClient(new LlmTransientException("503", retryAfter: null));
        var client = Unspaced(inner, recorder);

        var response = await client.CompleteAsync(AnyRequest, CancellationToken.None);

        Assert.Equal("recovered", response.Text);
        Assert.Equal(2, inner.Calls);
        Assert.Equal([TimeSpan.FromSeconds(1)], recorder.Delays);
    }

    [Fact]
    public async Task TheProvidersOwnRetryAfterHintIsPreferredToTheComputedWait()
    {
        var recorder = NewRecorder();
        var inner = new FailingLlmClient(
            new LlmRateLimitException("429", TimeSpan.FromSeconds(42)));
        var client = Unspaced(inner, recorder);

        await client.CompleteAsync(AnyRequest, CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(42)], recorder.Delays);
    }

    [Fact]
    public async Task APersistentlyRateLimitedCallGivesUpAfterItsBudgetRatherThanLoopingForever()
    {
        var recorder = NewRecorder();
        var inner = new FailingLlmClient(
            Enumerable.Range(0, 10)
                .Select(_ => (Exception)new LlmRateLimitException("429", retryAfter: null))
                .ToArray());
        var client = Unspaced(inner, recorder, maximumAttempts: 3);

        await Assert.ThrowsAsync<LlmRateLimitException>(
            () => client.CompleteAsync(AnyRequest, CancellationToken.None));

        Assert.Equal(3, inner.Calls);
        Assert.Equal(2, recorder.Delays.Count);
    }

    [Fact]
    public async Task TheGrowingWaitStopsGrowingAtItsCeiling()
    {
        var recorder = NewRecorder();
        var inner = new FailingLlmClient(
            Enumerable.Range(0, 10)
                .Select(_ => (Exception)new LlmTransientException("503", retryAfter: null))
                .ToArray());
        var client = new RateLimitedLlmClient(
            inner,
            requestsPerMinute: 60_000,
            maximumAttempts: 6,
            firstBackoff: TimeSpan.FromSeconds(2),
            maximumBackoff: TimeSpan.FromSeconds(8),
            timeProvider: recorder.Clock,
            delayAsync: recorder.RecordAsync);

        await Assert.ThrowsAsync<LlmTransientException>(
            () => client.CompleteAsync(AnyRequest, CancellationToken.None));

        Assert.Equal(
            [
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(8),
            ],
            recorder.Delays);
    }

    [Fact]
    public async Task AFailureThatIsNotWorthRetryingIsRaisedImmediately()
    {
        var recorder = NewRecorder();
        var inner = new FailingLlmClient(new LlmAuthenticationException("401"));
        var client = Unspaced(inner, recorder);

        await Assert.ThrowsAsync<LlmAuthenticationException>(
            () => client.CompleteAsync(AnyRequest, CancellationToken.None));

        Assert.Equal(1, inner.Calls);
        Assert.Empty(recorder.Delays);
    }

    [Fact]
    public async Task ASuccessfulCallIsNeitherDelayedNorRepeated()
    {
        var recorder = NewRecorder();
        var inner = new RecordingLlmClient("fine");
        var client = Unspaced(inner, recorder);

        await client.CompleteAsync(AnyRequest, CancellationToken.None);

        Assert.Equal(1, inner.Calls);
        Assert.Empty(recorder.Delays);
    }

    [Fact]
    public async Task SuccessiveCallsAreSpacedToStayWithinTheConfiguredRequestsPerMinute()
    {
        var recorder = NewRecorder();
        var inner = new RecordingLlmClient();
        var client = new RateLimitedLlmClient(
            inner,
            requestsPerMinute: 10,
            timeProvider: recorder.Clock,
            delayAsync: recorder.RecordAsync);

        await client.CompleteAsync(AnyRequest, CancellationToken.None);
        await client.CompleteAsync(AnyRequest, CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(6)], recorder.Delays);
    }

    [Fact]
    public async Task ACacheHitSpendsNoRateLimitBudget()
    {
        using var directory = new TemporaryDirectory();
        var provider = new RecordingLlmClient("answer");
        var limiter = new RateLimitedLlmClient(provider, requestsPerMinute: 60_000);
        var client = new CachingLlmClient(limiter, directory.Path, offline: false);

        await client.CompleteAsync(AnyRequest, CancellationToken.None);
        var callsAfterFirst = limiter.ProviderCalls;
        await client.CompleteAsync(AnyRequest, CancellationToken.None);

        Assert.Equal(1, callsAfterFirst);
        Assert.Equal(1, limiter.ProviderCalls);
        Assert.Equal(1, provider.Calls);
    }
}
