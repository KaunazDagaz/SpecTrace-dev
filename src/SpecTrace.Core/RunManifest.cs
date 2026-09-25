namespace SpecTrace.Core;

public sealed record RunManifest(
    string RunId,
    string DocumentId,
    string Provider,
    string Model,
    double Temperature,
    DateTimeOffset StartedAt,
    int PromptCount,
    int CacheHits,
    int InputTokens,
    int OutputTokens,
    string PipelineVersion,
    string GitSha);
