using System.Text.Json;
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
    private readonly List<string> _attempted = [];

    public int Attempts => _attempted.Count;

    public IReadOnlyList<string> Attempted => _attempted;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var target = $"{request.Method} {request.RequestUri}";
        _attempted.Add(target);

        throw new NetworkRefusedException(target);
    }
}

internal sealed class NetworkRefusedException : Exception
{
    public NetworkRefusedException(string target)
        : base($"No network: a request to {target} was attempted, and this handler refuses every request.")
    {
    }
}

internal static class Repository
{
    public static readonly string Root = FindRoot();

    public static string PathTo(params string[] segments) =>
        System.IO.Path.Combine([Root, .. segments]);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "SpecTrace.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find SpecTrace.sln above '{AppContext.BaseDirectory}'.");
    }
}

internal static class CoreIsolation
{
    public const string LlmAssemblyName = "SpecTrace.Llm";

    public static void AssertTheCoreProjectFileDeclaresNoLlmDependency()
    {
        var references = File.ReadAllLines(Repository.PathTo("src", "SpecTrace.Core", "SpecTrace.Core.csproj"))
            .Where(line => line.Contains("Reference", StringComparison.Ordinal))
            .Where(line => line.Contains(LlmAssemblyName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(references);
    }

    public static void AssertTheCompiledCoreAssemblyReferencesNoLlmAssembly()
    {
        var referenced = typeof(TextSpan).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty);

        Assert.DoesNotContain(LlmAssemblyName, referenced);
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

internal static class Readings
{
    public static void AssertNoneDisappeared(
        IReadOnlyList<CandidateRequirement> candidates,
        VerificationOutcome outcome)
    {
        foreach (var candidate in candidates)
        {
            var quote = TextNormalizer.Normalize(candidate.Quote);

            var inRegister = outcome.Register.Any(requirement =>
                TextNormalizer.Normalize(requirement.Text) == quote
                && requirement.Modality == candidate.Modality
                && requirement.Testability == candidate.Testability);

            var inRejected = outcome.Rejected.Any(rejected =>
                rejected.Quote == candidate.Quote
                && rejected.Modality == candidate.Modality
                && rejected.Testability == candidate.Testability);

            var inDecisions = outcome.Decisions.Any(decision => decision.Claims.Contains(candidate));

            Assert.True(
                inRegister || inRejected || inDecisions,
                $"The {RunArtifacts.Spell(candidate.Modality)} reading of '{candidate.Quote}' is in neither "
                + "the register, the rejected report nor the decision queue.");
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

internal sealed class RoutingLlmClient : ILlmClient
{
    private readonly Func<LlmRequest, string> _answer;

    public RoutingLlmClient(Func<LlmRequest, string> answer) => _answer = answer;

    public List<LlmRequest> Requests { get; } = [];

    public IEnumerable<LlmRequest> GenerationRequests =>
        Requests.Where(request => request.PromptSha256 == PromptFile.Generation.Sha256);

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        return Task.FromResult(new LlmResponse(_answer(request), InputTokens: 100, OutputTokens: 50, FromCache: false));
    }

    public static RoutingLlmClient For(string extractionAnswer, Func<string, string> generationAnswerForQuote) =>
        new(request => request.PromptSha256 == PromptFile.Extraction.Sha256
            ? extractionAnswer
            : generationAnswerForQuote(QuoteIn(request)));

    private static string QuoteIn(LlmRequest request)
    {
        const string Marker = "\nREQUIREMENT: ";

        return request.UserPrompt[(request.UserPrompt.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..];
    }
}

internal static class GenerationAnswerJson
{
    public static string Cases(params (string Type, string Title)[] cases) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["cases"] = cases.Select(generated => new Dictionary<string, object?>
            {
                ["title"] = generated.Title,
                ["type"] = generated.Type,
                ["precondition"] = "A target JSON document.",
                ["input"] = "A JSON Patch document with one operation.",
                ["expected_result"] = "The observable result the quote states.",
            }).ToList(),
            ["blocked_reason"] = null,
        });

    public static string Blocked(string reason) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["cases"] = Array.Empty<object>(),
            ["blocked_reason"] = reason,
        });
}

internal static class SchemaInspection
{
    public static readonly string[] PositionLikeNames =
    [
        "offset", "position", "location", "line", "column", "index", "start", "end", "span", "char",
    ];

    public static IEnumerable<string> PropertyNamesIn(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == "properties")
                {
                    foreach (var declared in property.Value.EnumerateObject())
                    {
                        yield return declared.Name;
                    }
                }

                foreach (var nested in PropertyNamesIn(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in PropertyNamesIn(item))
                {
                    yield return nested;
                }
            }
        }
    }
}
