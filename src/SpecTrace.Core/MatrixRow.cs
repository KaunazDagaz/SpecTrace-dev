namespace SpecTrace.Core;

public sealed record MatrixRow(
    string RequirementId,
    Modality Modality,
    string Section,
    IReadOnlyList<string> TestCaseIds,
    CoverageStatus Status);

public sealed record OrphanCase(
    string TestCaseId,
    IReadOnlyList<string> RequirementIds,
    IReadOnlyList<string> MissingRequirementIds);
