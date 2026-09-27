namespace SpecTrace.Core.Tests;

internal static class Fixtures
{
    public static GoldRequirement Gold(TextSpan span, Modality modality, int candidate = 1, int obligation = 1) =>
        new(candidate, obligation, $"gold quote {candidate}.{obligation}", modality, Testability.Testable, span, "1");

    public static GoldRequirement Gold(NormalizedDocument document, string quote, Modality modality, int candidate) =>
        new(candidate, 1, quote, modality, Testability.Testable, document.Resolve(quote).Span!.Value, "1");
}
