using System.Text.Json.Serialization;

namespace SpecTrace.Llm;

public sealed record CachedExchange(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("request")] CachedRequest Request,
    [property: JsonPropertyName("response")] CachedResponse Response);

public sealed record CachedRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("temperature")] double Temperature,
    [property: JsonPropertyName("maxOutputTokens")] int MaxOutputTokens,
    [property: JsonPropertyName("promptSha256")] string PromptSha256,
    [property: JsonPropertyName("systemPrompt")] string SystemPrompt,
    [property: JsonPropertyName("userPrompt")] string UserPrompt,
    [property: JsonPropertyName("jsonSchema")] string? JsonSchema);

public sealed record CachedResponse(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("inputTokens")] int InputTokens,
    [property: JsonPropertyName("outputTokens")] int OutputTokens);
