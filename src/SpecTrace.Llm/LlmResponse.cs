namespace SpecTrace.Llm;

public sealed record LlmResponse(
    string Text,
    int InputTokens,
    int OutputTokens,
    bool FromCache);
