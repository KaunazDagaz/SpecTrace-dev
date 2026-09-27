namespace SpecTrace.Core;

public sealed record AnnotatedFile(
    string Document,
    string RulesCommit,
    string Annotator,
    string AnnotatedAt,
    IReadOnlyList<AnnotatedCandidate> Candidates);

public sealed record AnnotatedCandidate(
    int Line,
    int Number,
    string Sentence,
    string Decision,
    IReadOnlyList<AnnotatedObligation> Obligations);

public sealed record AnnotatedObligation(
    int Line,
    string Quote,
    string Modality,
    string Testability,
    string Section)
{
    public bool IsBlank =>
        Quote.Length == 0 && Modality.Length == 0 && Testability.Length == 0 && Section.Length == 0;
}
