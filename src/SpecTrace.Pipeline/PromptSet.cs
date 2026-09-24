namespace SpecTrace.Pipeline;

public sealed record PromptSet(PromptFile Extraction, PromptFile Generation)
{
    public static PromptSet Embedded { get; } = new(PromptFile.Extraction, PromptFile.Generation);
}
