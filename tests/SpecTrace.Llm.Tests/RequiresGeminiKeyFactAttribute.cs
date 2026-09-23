namespace SpecTrace.Llm.Tests;

public sealed class RequiresGeminiKeyFactAttribute : FactAttribute
{
    public RequiresGeminiKeyFactAttribute()
    {
        if (GeminiLlmClient.ApiKeyFromEnvironment() is null)
        {
            Skip = $"{GeminiLlmClient.ApiKeyVariable} is not set, so the live provider was not called.";
        }
    }
}
