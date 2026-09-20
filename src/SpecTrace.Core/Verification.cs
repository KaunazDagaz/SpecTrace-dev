namespace SpecTrace.Core;

/// <summary>
/// The outcome of locating a quote in the source document.
/// </summary>
public enum Verification
{
    /// <summary>Found exactly once; the span is known.</summary>
    Exact,

    /// <summary>Found more than once with nothing to disambiguate it; no span is claimed.</summary>
    Ambiguous,

    /// <summary>Not found. The quote is not in the document and no span is guessed.</summary>
    Failed,
}
