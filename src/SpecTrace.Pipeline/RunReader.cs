using System.Text.Json;
using System.Text.Json.Serialization;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public sealed record RunContents(
    RunManifest Manifest,
    IReadOnlyList<Requirement> Register,
    IReadOnlyList<TestCase> Cases,
    IReadOnlyList<QueuedDecision> DecisionQueue,
    IReadOnlyList<RejectedQuote> Rejected)
{
    public IReadOnlyList<QueueEntry> QueueEntries =>
        DecisionQueue.Select(decision => new QueueEntry(decision.Item.Id, decision.RequirementId)).ToList();
}

public sealed class InvalidRunFilesException : Exception
{
    public InvalidRunFilesException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public static class RunReader
{
    public static readonly IReadOnlyList<string> PipelineFiles =
    [
        RunArtifacts.ManifestFile,
        RunArtifacts.RequirementsFile,
        RunArtifacts.RejectedQuotesFile,
        RunArtifacts.DecisionsFile,
        RunArtifacts.TestCasesFile,
        RunArtifacts.MatrixFile,
        RunArtifacts.MatrixHtmlFile,
    ];

    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static bool IsComplete(string directory) =>
        PipelineFiles.All(file => File.Exists(Path.Combine(directory, file)));

    public static async Task<RunContents> ReadAsync(string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var missing = PipelineFiles.Where(file => !File.Exists(Path.Combine(directory, file))).ToList();

        if (missing.Count > 0)
        {
            throw new InvalidRunFilesException(
                $"'{directory}' is not a complete pipeline run: {string.Join(", ", missing)} missing.");
        }

        var manifest = await ReadFileAsync<ManifestJson>(directory, RunArtifacts.ManifestFile, cancellationToken).ConfigureAwait(false);
        var requirements = await ReadFileAsync<List<RequirementJson>>(directory, RunArtifacts.RequirementsFile, cancellationToken).ConfigureAwait(false);
        var cases = await ReadFileAsync<List<TestCaseJson>>(directory, RunArtifacts.TestCasesFile, cancellationToken).ConfigureAwait(false);
        var decisions = await ReadFileAsync<List<DecisionJson>>(directory, RunArtifacts.DecisionsFile, cancellationToken).ConfigureAwait(false);
        var rejected = await ReadFileAsync<List<RejectedQuoteJson>>(directory, RunArtifacts.RejectedQuotesFile, cancellationToken).ConfigureAwait(false);

        return Convert(() => new RunContents(
            manifest.ToManifest(),
            requirements.Select(requirement => requirement.ToRequirement()).ToList().AsReadOnly(),
            cases.Select(testCase => testCase.ToTestCase()).ToList().AsReadOnly(),
            decisions.Select(decision => decision.ToQueuedDecision()).ToList().AsReadOnly(),
            rejected.Select(quote => quote.ToRejectedQuote()).ToList().AsReadOnly()));
    }

    private static async Task<T> ReadFileAsync<T>(string directory, string file, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, file);

        try
        {
            await using var stream = File.OpenRead(path);

            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidRunFilesException($"'{path}' holds null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidRunFilesException($"'{path}' is not a valid {file}: {exception.Message}", exception);
        }
    }

    private static T Convert<T>(Func<T> convert)
    {
        try
        {
            return convert();
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidRunFilesException($"The run's files do not describe a valid run: {exception.Message}", exception);
        }
    }

    private sealed record ManifestJson(
        [property: JsonPropertyName("run_id")] string RunId,
        [property: JsonPropertyName("document_id")] string DocumentId,
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
        [property: JsonPropertyName("prompt_count")] int PromptCount,
        [property: JsonPropertyName("cache_hits")] int CacheHits,
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens,
        [property: JsonPropertyName("pipeline_version")] string PipelineVersion,
        [property: JsonPropertyName("git_sha")] string GitSha)
    {
        public RunManifest ToManifest() => new(
            RunId, DocumentId, Provider, Model, Temperature, StartedAt,
            PromptCount, CacheHits, InputTokens, OutputTokens, PipelineVersion, GitSha);
    }

    private sealed record SpanJson(
        [property: JsonPropertyName("start")] int Start,
        [property: JsonPropertyName("end")] int End);

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
        public Requirement ToRequirement()
        {
            if (Spelled.Parse<Verification>(Verification, RunArtifacts.Spell) != Core.Verification.Exact)
            {
                throw new FormatException($"Requirement '{Id}' is in the register with verification '{Verification}'.");
            }

            return new Requirement(
                Id,
                DocumentId,
                Section,
                Spelled.Parse<Modality>(Modality, RunArtifacts.Spell),
                Text,
                new TextSpan(Span.Start, Span.End),
                Spelled.Parse<Testability>(Testability, RunArtifacts.Spell),
                TestabilityNote);
        }
    }

    private sealed record TestCaseJson(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("requirement_ids")] IReadOnlyList<string> RequirementIds,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("precondition")] string Precondition,
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("expected_result")] string ExpectedResult,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("review")] JsonElement? Review)
    {
        public TestCase ToTestCase()
        {
            if (Review is { ValueKind: not JsonValueKind.Null })
            {
                throw new FormatException($"Test case '{Id}' carries a review; the pipeline writes none.");
            }

            return new TestCase(
                Id,
                RequirementIds,
                Title,
                Spelled.Parse<CaseType>(Type, RunArtifacts.Spell),
                Precondition,
                Input,
                ExpectedResult,
                Spelled.Parse<ReviewStatus>(Status, RunArtifacts.Spell));
        }
    }

    private sealed record ClaimJson(
        [property: JsonPropertyName("modality")] string Modality,
        [property: JsonPropertyName("testability")] string Testability,
        [property: JsonPropertyName("testability_note")] string? TestabilityNote,
        [property: JsonPropertyName("quote")] string Quote)
    {
        public CandidateRequirement ToCandidate() => new(
            Spelled.Parse<Modality>(Modality, RunArtifacts.Spell),
            Quote,
            Spelled.Parse<Testability>(Testability, RunArtifacts.Spell),
            TestabilityNote);
    }

    private sealed record DecisionJson(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("quote")] string Quote,
        [property: JsonPropertyName("section")] string Section,
        [property: JsonPropertyName("reason")] string Reason,
        [property: JsonPropertyName("verification")] string Verification,
        [property: JsonPropertyName("requirement_id")] string? RequirementId,
        [property: JsonPropertyName("question")] string Question,
        [property: JsonPropertyName("blocked_reason")] string? BlockedReason,
        [property: JsonPropertyName("claims")] IReadOnlyList<ClaimJson> Claims,
        [property: JsonPropertyName("resolution")] string? Resolution)
    {
        public QueuedDecision ToQueuedDecision() => new(
            new DecisionQueueItem(Id, Quote, Section, Question, Resolution),
            Reason,
            Spelled.Parse<Verification>(Verification, RunArtifacts.Spell),
            Claims.Select(claim => claim.ToCandidate()).ToList().AsReadOnly(),
            RequirementId,
            BlockedReason);
    }

    private sealed record RejectedQuoteJson(
        [property: JsonPropertyName("quote")] string Quote,
        [property: JsonPropertyName("modality")] string Modality,
        [property: JsonPropertyName("testability")] string Testability,
        [property: JsonPropertyName("verification")] string Verification,
        [property: JsonPropertyName("reason")] string Reason)
    {
        public RejectedQuote ToRejectedQuote() => new(
            Quote,
            Spelled.Parse<Modality>(Modality, RunArtifacts.Spell),
            Spelled.Parse<Testability>(Testability, RunArtifacts.Spell),
            Reason);
    }
}
