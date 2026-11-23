namespace Presenta.Ai;

/// <summary>Limits shared by the prompt, the sanitizer and the tests (ADR-76).</summary>
public static class AiLimits
{
    /// <summary>Longest text the watch shows. The prompt asks for this many characters and the sanitizer enforces it.</summary>
    public const int MaxTextLength = 40;

    /// <summary>Smallest number of words a hard-truncated text may keep; with fewer the text is rejected.</summary>
    public const int MinWordsAfterTruncation = 3;

    /// <summary>Largest answer body read from the provider, in bytes.</summary>
    public const int MaxResponseBytes = 64 * 1024;
}

/// <summary>Why a request to the provider produced no usable text. It never carries provider text, URLs or the key.</summary>
public enum AiFailureKind
{
    /// <summary>The provider did not answer in time.</summary>
    Timeout,

    /// <summary>401 or 403: the key is not accepted.</summary>
    Unauthorized,

    /// <summary>429: too many requests.</summary>
    RateLimited,

    /// <summary>5xx.</summary>
    ServerError,

    /// <summary>The answer was not the expected JSON, had no choices, or had no text.</summary>
    Malformed,

    /// <summary>The provider could not be reached.</summary>
    Network,

    /// <summary>Any other refusal (other 4xx, a redirect).</summary>
    Rejected,
}

/// <summary>The two messages of one request. The user message holds only aggregate numbers and codes.</summary>
public sealed record AiPrompt(string System, string User);

/// <summary>Result of one request: the raw text of the model, or the kind of failure.</summary>
public sealed record AiTextResult(string? Text, AiFailureKind? Failure, TimeSpan? RetryAfter = null)
{
    public bool Succeeded => Failure is null;

    public static AiTextResult Success(string text) => new(text, null);

    public static AiTextResult Fail(AiFailureKind kind, TimeSpan? retryAfter = null) => new(null, kind, retryAfter);
}

/// <summary>Asks a language model for a short text. Implementations must honour the cancellation token and never log secrets.</summary>
public interface IAiTextClient
{
    Task<AiTextResult> GenerateAsync(AiPrompt prompt, CancellationToken cancellationToken);
}
