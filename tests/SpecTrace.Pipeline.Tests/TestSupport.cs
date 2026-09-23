using SpecTrace.Core;
using SpecTrace.Llm;

namespace SpecTrace.Pipeline.Tests;

internal static class Corpus
{
    public const string DocumentId = "rfc6902";

    public static readonly string Path =
        System.IO.Path.Combine(AppContext.BaseDirectory, "corpus", "rfc6902.txt");

    private static readonly Lazy<string> LazyRaw = new(() => File.ReadAllText(Path));

    public static string Raw => LazyRaw.Value;

    public static QuoteVerifier Verifier() =>
        new(DocumentId, NormalizedDocument.Create(Raw), SectionIndex.Build(Raw));
}

internal sealed class ScriptedLlmClient : ILlmClient
{
    private readonly string _text;

    public ScriptedLlmClient(string text) => _text = text;

    public int Calls { get; private set; }

    public LlmRequest? LastRequest { get; private set; }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;

        return Task.FromResult(new LlmResponse(_text, InputTokens: 100, OutputTokens: 50, FromCache: false));
    }
}

internal sealed class NoNetworkHandler : HttpMessageHandler
{
    public int Attempts { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Attempts++;
        throw new HttpRequestException("No network: this handler refuses every request.");
    }
}

internal sealed class ScratchDirectory : IDisposable
{
    public ScratchDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"spectrace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class ModelAnswer
{
    public static string With(params (string Modality, string Quote, string Testability)[] entries) =>
        System.Text.Json.JsonSerializer.Serialize(entries.Select(entry => new Dictionary<string, object?>
        {
            ["modality"] = entry.Modality,
            ["quote"] = entry.Quote,
            ["testability"] = entry.Testability,
            ["testability_note"] = null,
        }));
}
