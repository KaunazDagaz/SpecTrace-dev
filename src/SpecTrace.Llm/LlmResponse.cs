namespace SpecTrace.Llm;

public sealed record LlmResponse(
    string Text,
    int InputTokens,
    int OutputTokens,
    bool FromCache,
    string? FinishReason = null)
{
    public const string CompleteFinishReason = "STOP";
}
