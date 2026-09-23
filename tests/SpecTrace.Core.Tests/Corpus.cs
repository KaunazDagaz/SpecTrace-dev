namespace SpecTrace.Core.Tests;

internal static class Corpus
{
    public const int RawLength = 26405;

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
