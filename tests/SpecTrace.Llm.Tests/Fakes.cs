using System.Net;

namespace SpecTrace.Llm.Tests;

internal sealed class RecordingLlmClient : ILlmClient
{
    private readonly Func<LlmRequest, LlmResponse> _answer;

    public RecordingLlmClient(string text = "{}", Func<LlmRequest, LlmResponse>? answer = null) =>
        _answer = answer ?? (_ => new LlmResponse(text, InputTokens: 11, OutputTokens: 7, FromCache: false));

    public int Calls { get; private set; }

    public List<LlmRequest> Requests { get; } = [];

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        Requests.Add(request);

        return Task.FromResult(_answer(request));
    }
}

internal sealed class UnreachableLlmClient : ILlmClient
{
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The inner client was called when it should not have been.");
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;

    public StubHttpMessageHandler(params HttpResponseMessage[] responses) =>
        _responses = new Queue<HttpResponseMessage>(responses);

    public List<string> RequestBodies { get; } = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("The handler ran out of scripted responses.");
        }

        return _responses.Dequeue();
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}

internal sealed class OfflineHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        throw new HttpRequestException("No network: this handler refuses every request.");
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);

    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}

internal sealed class DelayRecorder
{
    public ManualTimeProvider Clock { get; } = new();

    public List<TimeSpan> Delays { get; } = [];

    public Task RecordAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Delays.Add(delay);
        Clock.Advance(delay);

        return Task.CompletedTask;
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"spectrace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public int FileCount => Directory.Exists(Path) ? Directory.GetFiles(Path).Length : 0;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
