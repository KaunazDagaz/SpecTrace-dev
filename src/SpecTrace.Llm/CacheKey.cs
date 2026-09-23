using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace SpecTrace.Llm;

public static class CacheKey
{
    public static string For(LlmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("model", request.Model);
            writer.WriteString("systemPrompt", request.SystemPrompt);
            writer.WriteString("userPrompt", request.UserPrompt);
            writer.WriteNumber("temperature", request.Temperature);
            writer.WriteNumber("maxOutputTokens", request.MaxOutputTokens);

            if (request.JsonSchema is null)
            {
                writer.WriteNull("jsonSchema");
            }
            else
            {
                writer.WriteString("jsonSchema", request.JsonSchema);
            }

            writer.WriteString("promptSha256", request.PromptSha256);
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }
}
