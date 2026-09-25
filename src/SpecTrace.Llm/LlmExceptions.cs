namespace SpecTrace.Llm;

public class LlmException : Exception
{
    public LlmException(string message)
        : base(message)
    {
    }

    public LlmException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

public abstract class LlmRetryableException : LlmException
{
    protected LlmRetryableException(string message, TimeSpan? retryAfter)
        : base(message) => RetryAfter = retryAfter;

    public TimeSpan? RetryAfter { get; }
}

public sealed class LlmRateLimitException : LlmRetryableException
{
    public LlmRateLimitException(string message, TimeSpan? retryAfter)
        : base(message, retryAfter)
    {
    }
}

public sealed class LlmQuotaExhaustedException : LlmException
{
    public LlmQuotaExhaustedException(string message, string quotaId, string? limit)
        : base(message)
    {
        QuotaId = quotaId;
        Limit = limit;
    }

    public string QuotaId { get; }

    public string? Limit { get; }
}

public sealed class LlmTransientException : LlmRetryableException
{
    public LlmTransientException(string message, TimeSpan? retryAfter)
        : base(message, retryAfter)
    {
    }
}

public sealed class OfflineCacheMissException : LlmException
{
    public OfflineCacheMissException(string cacheKey, string cachePath, string model, string promptSha256)
        : this(cacheKey, cachePath, model, promptSha256, call: null, innerException: null)
    {
    }

    private OfflineCacheMissException(
        string cacheKey,
        string cachePath,
        string model,
        string promptSha256,
        string? call,
        Exception? innerException)
        : base(Describe(cacheKey, cachePath, model, promptSha256, call), innerException)
    {
        CacheKey = cacheKey;
        CachePath = cachePath;
        Model = model;
        PromptSha256 = promptSha256;
        Call = call;
    }

    public string CacheKey { get; }

    public string CachePath { get; }

    public string Model { get; }

    public string PromptSha256 { get; }

    public string? Call { get; }

    public OfflineCacheMissException During(string call)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(call);

        return new OfflineCacheMissException(CacheKey, CachePath, Model, PromptSha256, call, this);
    }

    private static string Describe(string cacheKey, string cachePath, string model, string promptSha256, string? call) =>
        $"Offline replay has no recorded response for {call ?? "this request"}.\n"
        + $"  model          {model}\n"
        + $"  prompt sha256  {promptSha256}\n"
        + $"  cache key      {cacheKey}\n"
        + $"  expected at    {cachePath}\n"
        + "The committed cache does not cover this request, so something the request is built from changed "
        + "after the cache was recorded: a prompt file, the model, the schema, the document or a requirement's "
        + "text. Nothing was sent to the provider. To record it, run once online, with GEMINI_API_KEY set and "
        + "without --offline or SPECTRACE_OFFLINE, then review and commit the new files under cache/.";
}

public sealed class LlmAuthenticationException : LlmException
{
    public LlmAuthenticationException(string message)
        : base(message)
    {
    }
}

public sealed class LlmResponseException : LlmException
{
    public LlmResponseException(string message)
        : base(message)
    {
    }

    public LlmResponseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
