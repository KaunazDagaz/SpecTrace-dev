namespace SpecTrace.Pipeline;

public sealed record PromptSet(PromptFile Extraction, PromptFile Generation, PromptFile Baseline)
{
    public static PromptSet Embedded { get; } = new(PromptFile.Extraction, PromptFile.Generation, PromptFile.Baseline);
}
