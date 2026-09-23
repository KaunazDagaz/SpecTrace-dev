namespace SpecTrace.Llm;

public class LlmException : Exception
{
    public LlmException(string message)
        : base(message)
    {
    }

    public LlmException(string message, Exception innerException)
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
    public OfflineCacheMissException(string cacheKey, string cachePath)
        : base($"SPECTRACE_OFFLINE is set and no cached response exists for request '{cacheKey}'. "
               + $"Expected it at '{cachePath}'. Re-run with a key and without SPECTRACE_OFFLINE to "
               + "record it, and commit the new cache entry.")
    {
        CacheKey = cacheKey;
        CachePath = cachePath;
    }

    public string CacheKey { get; }

    public string CachePath { get; }
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
