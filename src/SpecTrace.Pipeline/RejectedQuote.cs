using SpecTrace.Core;

namespace SpecTrace.Pipeline;

public sealed record RejectedQuote(
    string Quote,
    Modality Modality,
    Testability Testability,
    string Reason)
{
    public const string NotFound = "not_found_in_source";

    public const string EmptyQuote = "empty_quote";
}
