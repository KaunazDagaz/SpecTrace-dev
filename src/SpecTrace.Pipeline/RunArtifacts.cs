using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public static class RunArtifacts
{
    public const string ManifestFile = "manifest.json";
    public const string RequirementsFile = "requirements.json";
    public const string RejectedQuotesFile = "rejected-quotes.json";
    public const string DecisionsFile = "decisions.json";
    public const string TestCasesFile = "test-cases.json";
    public const string MatrixFile = "matrix.json";
    public const string MatrixHtmlFile = "matrix.html";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly UTF8Encoding Utf8WithoutMark = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteAsync(
        VerificationOutcome outcome,
        string directory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);

        await WriteVerificationAsync(outcome, outcome.Decisions, directory, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteRunAsync(
        PipelineRunResult run,
        string directory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);

        await WriteFileAsync(
            Path.Combine(directory, ManifestFile),
            ManifestJson.From(run.Manifest),
            cancellationToken).ConfigureAwait(false);

        await WriteVerificationAsync(run.Extraction.Outcome, run.DecisionQueue, directory, cancellationToken)
            .ConfigureAwait(false);

        await WriteFileAsync(
            Path.Combine(directory, TestCasesFile),
            run.Cases.Select(TestCaseJson.From).ToList(),
            cancellationToken).ConfigureAwait(false);

        await WriteMatrixAsync(
            run.DocumentId,
            run.Register,
            run.Cases,
            run.HumanDecisions,
            run.DecisionQueue,
            directory,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteMatrixAsync(
        string documentId,
        IReadOnlyList<Requirement> register,
        IReadOnlyList<TestCase> cases,
        IReadOnlyList<HumanCoverageDecision> humanDecisions,
        IReadOnlyList<QueuedDecision> decisionQueue,
        string directory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(decisionQueue);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var matrix = TraceabilityMatrix.Build(register, cases, humanDecisions);

        Directory.CreateDirectory(directory);

        await WriteFileAsync(
            Path.Combine(directory, MatrixFile),
            MatrixJson.From(documentId, matrix),
            cancellationToken).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(directory, MatrixHtmlFile),
            MatrixHtml.Render(documentId, register, cases, matrix, decisionQueue),
            Utf8WithoutMark,
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

    public static string Spell(Verification verification) => verification switch
    {
        Verification.Exact => "exact",
        Verification.Ambiguous => "ambiguous",
        Verification.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(verification), verification, null),
    };

    public static string Spell(CaseType type) => type switch
    {
        CaseType.Positive => "positive",
        CaseType.Negative => "negative",
        CaseType.Boundary => "boundary",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static string Spell(ReviewStatus status) => status switch
    {
        ReviewStatus.Proposed => "proposed",
        ReviewStatus.Accepted => "accepted",
        ReviewStatus.Edited => "edited",
        ReviewStatus.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string Spell(CoverageStatus status) => status switch
    {
        CoverageStatus.Covered => "covered",
        CoverageStatus.Gap => "gap",
        CoverageStatus.DeferredByHuman => "deferred_by_human",
        CoverageStatus.NotTestable => "not_testable",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static async Task WriteVerificationAsync(
        VerificationOutcome outcome,
        IReadOnlyList<QueuedDecision> decisionQueue,
        string directory,
        CancellationToken cancellationToken)
    {
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
            decisionQueue.Select(DecisionJson.From).ToList(),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteFileAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken).ConfigureAwait(false);
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
        public static ManifestJson From(RunManifest manifest) => new(
            manifest.RunId,
            manifest.DocumentId,
            manifest.Provider,
            manifest.Model,
            manifest.Temperature,
            manifest.StartedAt.ToUniversalTime(),
            manifest.PromptCount,
            manifest.CacheHits,
            manifest.InputTokens,
            manifest.OutputTokens,
            manifest.PipelineVersion,
            manifest.GitSha);
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
        [property: JsonPropertyName("reason")] string Reason,
        [property: JsonPropertyName("verification")] string Verification,
        [property: JsonPropertyName("requirement_id")] string? RequirementId,
        [property: JsonPropertyName("question")] string Question,
        [property: JsonPropertyName("blocked_reason")] string? BlockedReason,
        [property: JsonPropertyName("claims")] IReadOnlyList<ClaimJson> Claims,
        [property: JsonPropertyName("resolution")] string? Resolution)
    {
        public static DecisionJson From(QueuedDecision decision) => new(
            decision.Item.Id,
            decision.Item.Quote,
            decision.Item.Section,
            decision.Reason,
            Spell(decision.Verification),
            decision.RequirementId,
            decision.Item.Question,
            decision.BlockedReason,
            decision.Claims.Select(ClaimJson.From).ToList(),
            decision.Item.Resolution);
    }

    private sealed record ClaimJson(
        [property: JsonPropertyName("modality")] string Modality,
        [property: JsonPropertyName("testability")] string Testability,
        [property: JsonPropertyName("testability_note")] string? TestabilityNote,
        [property: JsonPropertyName("quote")] string Quote)
    {
        public static ClaimJson From(CandidateRequirement claim) => new(
            Spell(claim.Modality),
            Spell(claim.Testability),
            claim.TestabilityNote,
            claim.Quote);
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
        [property: JsonPropertyName("review")] object? Review)
    {
        public static TestCaseJson From(TestCase testCase) => new(
            testCase.Id,
            testCase.RequirementIds,
            testCase.Title,
            Spell(testCase.Type),
            testCase.Precondition,
            testCase.Input,
            testCase.ExpectedResult,
            Spell(testCase.Status),
            Review: null);
    }

    private sealed record MatrixJson(
        [property: JsonPropertyName("document_id")] string DocumentId,
        [property: JsonPropertyName("rows")] IReadOnlyList<MatrixRowJson> Rows,
        [property: JsonPropertyName("orphans")] IReadOnlyList<OrphanJson> Orphans)
    {
        public static MatrixJson From(string documentId, TraceabilityMatrix matrix) => new(
            documentId,
            matrix.Rows.Select(MatrixRowJson.From).ToList(),
            matrix.Orphans.Select(OrphanJson.From).ToList());
    }

    private sealed record MatrixRowJson(
        [property: JsonPropertyName("requirement_id")] string RequirementId,
        [property: JsonPropertyName("modality")] string Modality,
        [property: JsonPropertyName("section")] string Section,
        [property: JsonPropertyName("test_case_ids")] IReadOnlyList<string> TestCaseIds,
        [property: JsonPropertyName("status")] string Status)
    {
        public static MatrixRowJson From(MatrixRow row) => new(
            row.RequirementId,
            Spell(row.Modality),
            row.Section,
            row.TestCaseIds,
            Spell(row.Status));
    }

    private sealed record OrphanJson(
        [property: JsonPropertyName("test_case_id")] string TestCaseId,
        [property: JsonPropertyName("requirement_ids")] IReadOnlyList<string> RequirementIds,
        [property: JsonPropertyName("missing_requirement_ids")] IReadOnlyList<string> MissingRequirementIds)
    {
        public static OrphanJson From(OrphanCase orphan) => new(
            orphan.TestCaseId,
            orphan.RequirementIds,
            orphan.MissingRequirementIds);
    }
}
