using System.Text.Json;
using System.Text.Json.Serialization;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public static class RunArtifacts
{
    public const string RequirementsFile = "requirements.json";
    public const string RejectedQuotesFile = "rejected-quotes.json";
    public const string DecisionsFile = "decisions.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task WriteAsync(
        VerificationOutcome outcome,
        string directory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);

        await WriteFileAsync(
            Path.Combine(directory, RequirementsFile),
            outcome.Register.Select(RequirementJson.From).ToList(),
            cancellationToken).ConfigureAwait(false);

        await WriteFileAsync(
            Path.Combine(directory, RejectedQuotesFile),
            outcome.Rejected.Select(RejectedQuoteJson.From).ToList(),
            cancellationToken).ConfigureAwait(false);

        await WriteFileAsync(
            Path.Combine(directory, DecisionsFile),
            outcome.Decisions.Select(DecisionJson.From).ToList(),
            cancellationToken).ConfigureAwait(false);
    }

    public static string Spell(Modality modality) => modality switch
    {
        Modality.Must => "MUST",
        Modality.MustNot => "MUST_NOT",
        Modality.Should => "SHOULD",
        Modality.ShouldNot => "SHOULD_NOT",
        Modality.May => "MAY",
        _ => throw new ArgumentOutOfRangeException(nameof(modality), modality, null),
    };

    public static string Spell(Testability testability) => testability switch
    {
        Testability.Testable => "testable",
        Testability.NeedsHumanDecision => "needs_human_decision",
        Testability.NotTestable => "not_testable",
        _ => throw new ArgumentOutOfRangeException(nameof(testability), testability, null),
    };

    private static async Task WriteFileAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken).ConfigureAwait(false);
    }

    private sealed record RequirementJson(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("document_id")] string DocumentId,
        [property: JsonPropertyName("section")] string Section,
        [property: JsonPropertyName("modality")] string Modality,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("span")] SpanJson Span,
        [property: JsonPropertyName("testability")] string Testability,
        [property: JsonPropertyName("testability_note")] string? TestabilityNote,
        [property: JsonPropertyName("verification")] string Verification)
    {
        public static RequirementJson From(Requirement requirement) => new(
            requirement.Id,
            requirement.DocumentId,
            requirement.Section,
            Spell(requirement.Modality),
            requirement.Text,
            new SpanJson(requirement.Span.Start, requirement.Span.End),
            Spell(requirement.Testability),
            requirement.TestabilityNote,
            "exact");
    }

    private sealed record SpanJson(
        [property: JsonPropertyName("start")] int Start,
        [property: JsonPropertyName("end")] int End);

    private sealed record RejectedQuoteJson(
        [property: JsonPropertyName("quote")] string Quote,
        [property: JsonPropertyName("modality")] string Modality,
        [property: JsonPropertyName("testability")] string Testability,
        [property: JsonPropertyName("verification")] string Verification,
        [property: JsonPropertyName("reason")] string Reason)
    {
        public static RejectedQuoteJson From(RejectedQuote rejected) => new(
            rejected.Quote,
            Spell(rejected.Modality),
            Spell(rejected.Testability),
            "failed",
            rejected.Reason);
    }

    private sealed record DecisionJson(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("quote")] string Quote,
        [property: JsonPropertyName("section")] string Section,
        [property: JsonPropertyName("question")] string Question,
        [property: JsonPropertyName("verification")] string Verification,
        [property: JsonPropertyName("resolution")] string? Resolution)
    {
        public static DecisionJson From(DecisionQueueItem item) => new(
            item.Id,
            item.Quote,
            item.Section,
            item.Question,
            "ambiguous",
            item.Resolution);
    }
}
