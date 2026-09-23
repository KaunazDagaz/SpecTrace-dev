using System.Text.Json;
using SpecTrace.Core;
using SpecTrace.Llm;

namespace SpecTrace.Pipeline;

public sealed class RequirementExtractor
{
    public const int DefaultMaxOutputTokens = 16_384;

    private readonly ILlmClient _client;
    private readonly string _model;
    private readonly int _maxOutputTokens;

    public RequirementExtractor(
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

    public LlmRequest RequestFor(string documentId, string rawDocument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(rawDocument);

        return new LlmRequest(
            SystemPrompt: PromptFile.Extraction.Text,
            UserPrompt: $"DOCUMENT ID: {documentId}\n\n{rawDocument}",
            Model: _model,
            Temperature: 0,
            MaxOutputTokens: _maxOutputTokens,
            JsonSchema: ExtractionSchema.Json,
            PromptSha256: PromptFile.Extraction.Sha256);
    }

    public async Task<ExtractionResult> ExtractAsync(
        string documentId,
        string rawDocument,
        CancellationToken cancellationToken)
    {
        var request = RequestFor(documentId, rawDocument);
        var response = await _client.CompleteAsync(request, cancellationToken).ConfigureAwait(false);

        return new ExtractionResult(Parse(response.Text), response);
    }

    public static IReadOnlyList<CandidateRequirement> Parse(string responseText)
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
                "The extraction response is not valid JSON. It is requested with a response "
                + "schema, so this means the provider broke its own contract.",
                exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new LlmResponseException(
                    $"The extraction response is a JSON {document.RootElement.ValueKind}, not the "
                    + "array the schema declares.");
            }

            var candidates = new List<CandidateRequirement>();

            foreach (var element in document.RootElement.EnumerateArray())
            {
                candidates.Add(ReadCandidate(element));
            }

            return candidates;
        }
    }

    private static CandidateRequirement ReadCandidate(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new LlmResponseException(
                $"An extraction entry is a JSON {element.ValueKind}, not an object.");
        }

        var quote = element.TryGetProperty("quote", out var quoteElement)
            ? quoteElement.GetString() ?? string.Empty
            : throw new LlmResponseException("An extraction entry has no 'quote'.");

        var note = element.TryGetProperty("testability_note", out var noteElement)
            && noteElement.ValueKind == JsonValueKind.String
            ? noteElement.GetString()
            : null;

        return new CandidateRequirement(
            ReadModality(element),
            quote,
            ReadTestability(element),
            note);
    }

    private static Modality ReadModality(JsonElement element)
    {
        var value = element.TryGetProperty("modality", out var modality)
            ? modality.GetString()
            : throw new LlmResponseException("An extraction entry has no 'modality'.");

        return value switch
        {
            "MUST" => Modality.Must,
            "MUST_NOT" => Modality.MustNot,
            "SHOULD" => Modality.Should,
            "SHOULD_NOT" => Modality.ShouldNot,
            "MAY" => Modality.May,
            _ => throw new LlmResponseException(
                $"'{value}' is not one of the five modalities the schema declares."),
        };
    }

    private static Testability ReadTestability(JsonElement element)
    {
        var value = element.TryGetProperty("testability", out var testability)
            ? testability.GetString()
            : throw new LlmResponseException("An extraction entry has no 'testability'.");

        return value switch
        {
            "testable" => Testability.Testable,
            "needs_human_decision" => Testability.NeedsHumanDecision,
            "not_testable" => Testability.NotTestable,
            _ => throw new LlmResponseException(
                $"'{value}' is not one of the three testability values the schema declares."),
        };
    }
}

public sealed record ExtractionResult(
    IReadOnlyList<CandidateRequirement> Candidates,
    LlmResponse Response);
