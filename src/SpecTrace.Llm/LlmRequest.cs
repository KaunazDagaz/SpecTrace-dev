namespace SpecTrace.Llm;

public sealed record LlmRequest(
    string SystemPrompt,
    string UserPrompt,
    string Model,
    double Temperature,
    int MaxOutputTokens,
    string? JsonSchema,
    string PromptSha256);
