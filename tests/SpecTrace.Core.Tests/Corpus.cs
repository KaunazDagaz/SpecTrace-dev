namespace SpecTrace.Core.Tests;

/// <summary>
/// The real corpus document, loaded once for the offset-map and section-index tests.
/// </summary>
/// <remarks>
/// These tests run against RFC 6902 itself rather than against strings written for the
/// test. An offset map is exactly the kind of code that passes convincing tests over
/// input its author shaped, and then fails on a hard-wrapped line in a real file.
/// </remarks>
internal static class Corpus
{
    /// <summary>Size of <c>corpus/rfc6902.txt</c> in bytes, as committed.</summary>
    public const int RawLength = 26405;

    /// <summary>Raw offset of the first non-blank line — the file opens with six blank lines.</summary>
    public const int FirstContentOffset = 6;

    private static readonly Lazy<string> LazyRaw = new(Load);
    private static readonly Lazy<NormalizedDocument> LazyDocument = new(() => NormalizedDocument.Create(Raw));
    private static readonly Lazy<SectionIndex> LazyIndex = new(() => SectionIndex.Build(Raw));

    public static string Raw => LazyRaw.Value;

    public static NormalizedDocument Document => LazyDocument.Value;

    public static SectionIndex Index => LazyIndex.Value;

    private static string Load() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "corpus", "rfc6902.txt"));
}
