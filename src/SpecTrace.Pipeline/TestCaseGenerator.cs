using System.Text.Json;
using SpecTrace.Core;
using SpecTrace.Llm;

namespace SpecTrace.Pipeline;

public sealed class TestCaseGenerator
{
    public const int DefaultMaxOutputTokens = 8_192;

    public const int MaximumCases = 3;

    private const string CasesProperty = "cases";
    private const string BlockedReasonProperty = "blocked_reason";

    private static readonly string[] CaseProperties = ["title", "type", "precondition", "input", "expected_result"];

    private readonly ILlmClient _client;
    private readonly string _model;
    private readonly int _maxOutputTokens;

    public TestCaseGenerator(
        ILlmClient client,
        string model,
        int maxOutputTokens = DefaultMaxOutputTokens)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _client = client;
        _model = model;
        _maxOutputTokens = maxOutputTokens;
    }

    public static string UserPromptFor(Requirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        return $"SECTION: {requirement.Section}\nREQUIREMENT: {TextNormalizer.Normalize(requirement.Text)}";
    }

    public static string CaseIdFor(string requirementId, int ordinal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirementId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ordinal);

        var hash = requirementId[(requirementId.LastIndexOf('-') + 1)..];

        return $"TC-{hash}-{ordinal:D2}";
    }

    public LlmRequest RequestFor(Requirement requirement) =>
        new(
            SystemPrompt: PromptFile.Generation.Text,
            UserPrompt: UserPromptFor(requirement),
            Model: _model,
            Temperature: 0,
            MaxOutputTokens: _maxOutputTokens,
            JsonSchema: GenerationSchema.Json,
            PromptSha256: PromptFile.Generation.Sha256);

    public async Task<Generation> GenerateAsync(Requirement requirement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        if (requirement.Testability != Testability.Testable)
        {
            throw new ArgumentException(
                $"Requirement '{requirement.Id}' is not testable by the model's classification, so no "
                + "case may be generated for it (REQ-GEN-01).",
                nameof(requirement));
        }

        var response = await _client
            .CompleteAsync(RequestFor(requirement), cancellationToken)
            .ConfigureAwait(false);

        GenerationAnswer answer;

        try
        {
            answer = Parse(response.Text);
        }
        catch (LlmResponseException exception)
        {
            throw new LlmResponseException(
                $"The generation response for {requirement.Id} does not match the schema, so the call "
                + $"failed and none of its cases are used. {exception.Message}",
                exception);
        }

        var cases = answer.Cases
            .Select((generated, index) => new TestCase(
                CaseIdFor(requirement.Id, index + 1),
                [requirement.Id],
                generated.Title,
                generated.Type,
                generated.Precondition,
                generated.Input,
                generated.ExpectedResult,
                ReviewStatus.Proposed))
            .ToList();

        return new Generation(requirement.Id, cases, answer.BlockedReason, response);
    }

    public static GenerationAnswer Parse(string responseText)
    {
        ArgumentNullException.ThrowIfNull(responseText);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(responseText);
        }
        catch (JsonException exception)
        {
            throw new LlmResponseException(
                "The generation response is not valid JSON. It is requested with a response schema, "
                + "so this means the provider broke its own contract.",
                exception);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new LlmResponseException(
                    $"The generation response is a JSON {root.ValueKind}, not the object the schema declares.");
            }

            RefuseUnknownProperties(root, [CasesProperty, BlockedReasonProperty], "The generation response");

            if (!root.TryGetProperty(CasesProperty, out var casesElement)
                || casesElement.ValueKind != JsonValueKind.Array)
            {
                throw new LlmResponseException("The generation response has no 'cases' array.");
            }

            if (casesElement.GetArrayLength() > MaximumCases)
            {
                throw new LlmResponseException(
                    $"The generation response has {casesElement.GetArrayLength()} cases; the prompt allows "
                    + $"at most {MaximumCases}.");
            }

            var cases = casesElement.EnumerateArray().Select(ReadCase).ToList();
            var blockedReason = ReadBlockedReason(root);

            if (cases.Count == 0 && blockedReason is null)
            {
                throw new LlmResponseException(
                    "The generation response has no cases and no 'blocked_reason'. An empty answer must say why.");
            }

            if (cases.Count > 0 && blockedReason is not null)
            {
                throw new LlmResponseException(
                    "The generation response has cases and also a 'blocked_reason'. It cannot be both "
                    + "blocked and answered.");
            }

            return new GenerationAnswer(cases, blockedReason);
        }
    }

    private static GeneratedCase ReadCase(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new LlmResponseException($"A generated case is a JSON {element.ValueKind}, not an object.");
        }

        RefuseUnknownProperties(element, CaseProperties, "A generated case");

        var title = ReadString(element, "title", mayBeBlank: false);
        var type = ReadString(element, "type", mayBeBlank: false) switch
        {
            "positive" => CaseType.Positive,
            "negative" => CaseType.Negative,
            "boundary" => CaseType.Boundary,
            var other => throw new LlmResponseException(
                $"'{other}' is not one of the three case types the schema declares."),
        };

        return new GeneratedCase(
            title,
            type,
            ReadString(element, "precondition", mayBeBlank: true),
            ReadString(element, "input", mayBeBlank: false),
            ReadString(element, "expected_result", mayBeBlank: false));
    }

    private static string ReadString(JsonElement element, string name, bool mayBeBlank)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw new LlmResponseException($"A generated case has no '{name}'.");
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new LlmResponseException($"A generated case's '{name}' is a JSON {value.ValueKind}, not a string.");
        }

        var text = value.GetString()!;

        if (!mayBeBlank && string.IsNullOrWhiteSpace(text))
        {
            throw new LlmResponseException($"A generated case's '{name}' is blank.");
        }

        return text;
    }

    private static string? ReadBlockedReason(JsonElement root)
    {
        if (!root.TryGetProperty(BlockedReasonProperty, out var reason) || reason.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (reason.ValueKind != JsonValueKind.String)
        {
            throw new LlmResponseException(
                $"The generation response's 'blocked_reason' is a JSON {reason.ValueKind}, not a string or null.");
        }

        var text = reason.GetString()!;

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static void RefuseUnknownProperties(JsonElement element, string[] allowed, string what)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new LlmResponseException(
                    $"{what} has a property '{property.Name}' that the schema does not declare.");
            }
        }
    }
}

public sealed record GeneratedCase(
    string Title,
    CaseType Type,
    string Precondition,
    string Input,
    string ExpectedResult);

public sealed record GenerationAnswer(
    IReadOnlyList<GeneratedCase> Cases,
    string? BlockedReason);

public sealed record Generation(
    string RequirementId,
    IReadOnlyList<TestCase> Cases,
    string? BlockedReason,
    LlmResponse Response);
