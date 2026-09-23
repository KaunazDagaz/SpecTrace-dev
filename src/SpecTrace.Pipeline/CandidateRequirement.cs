using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public sealed record CandidateRequirement(
    Modality Modality,
    string Quote,
    Testability Testability,
    string? TestabilityNote);
