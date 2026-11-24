using System.Net;

namespace Presenta.Ai;

/// <summary>Why the AI cannot be used on this computer.</summary>
public enum AiAvailability
{
    /// <summary>The three settings are present and valid.</summary>
    Available,

    /// <summary>At least one of the three environment variables is missing or empty.</summary>
    MissingVariables,

    /// <summary>The base address is not an absolute http or https address, or contains credentials.</summary>
    InvalidAddress,

    /// <summary>The base address is plain http to a host that is not this computer.</summary>
    InsecureAddress,
}

/// <summary>
/// Where to send requests. The key lives only here, in memory: <see cref="ToString"/> hides it, and nothing in this
/// project writes it anywhere.
/// </summary>
public sealed class AiEndpointOptions
{
    private readonly string _apiKey;

    public AiEndpointOptions(Uri baseUrl, string model, string apiKey)
    {
        BaseUrl = baseUrl;
        Model = model;
        _apiKey = apiKey;
    }

    public Uri BaseUrl { get; }

    public string Model { get; }

    /// <summary>Maximum time to wait for the provider for one answer, including the one adaptive retry (ADR-80).</summary>
    public TimeSpan Timeout { get; init; } = AiDefaults.Timeout;

    /// <summary>
    /// Token limit sent to the provider. The tip itself is capped at 40 characters by the sanitizer; the limit is large
    /// because reasoning models count their hidden reasoning inside it.
    /// </summary>
    public int MaxTokens { get; init; } = AiDefaults.MaxTokens;

    /// <summary>Name of the token-limit parameter to start with. The client may switch it after a 400 (ADR-80).</summary>
    public AiTokenLimitParameter TokenLimitParameter { get; init; } = AiTokenLimitParameter.MaxCompletionTokens;

    /// <summary>Temperature to send, or null to leave it out (the default: reasoning models reject it).</summary>
    public double? Temperature { get; init; }

    /// <summary>Value of <c>reasoning_effort</c> to send, or null to leave it out (the default).</summary>
    public string? ReasoningEffort { get; init; }

    /// <summary>The key, for the Authorization header only.</summary>
    internal string ApiKey => _apiKey;

    public override string ToString() => $"AiEndpointOptions {{ BaseUrl = {BaseUrl.Scheme}://{BaseUrl.Authority}, Model = {Model}, ApiKey = *** }}";
}

/// <summary>Result of reading the three environment variables.</summary>
public sealed record AiConfigurationResult(AiAvailability Availability, AiEndpointOptions? Options)
{
    public bool IsAvailable => Availability == AiAvailability.Available;
}

/// <summary>Which JSON field carries the token limit.</summary>
public enum AiTokenLimitParameter
{
    /// <summary><c>max_completion_tokens</c>: what current OpenAI chat models expect.</summary>
    MaxCompletionTokens,

    /// <summary><c>max_tokens</c>: older models and many other compatible servers.</summary>
    MaxTokens,
}

/// <summary>Defaults of the optional request settings (ADR-80).</summary>
public static class AiDefaults
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public const int MaxTokens = 400;

    public const int MinTokensAllowed = 16;

    public const int MaxTokensAllowed = 8000;

    public const int MinTimeoutSeconds = 1;

    public const int MaxTimeoutSeconds = 30;

    public const int MaxEffortLength = 24;
}

/// <summary>
/// Reads <c>PRESENTA_AI_BASE_URL</c>, <c>PRESENTA_AI_MODEL</c> and <c>PRESENTA_AI_API_KEY</c> (ADR-73) and the optional
/// request settings (ADR-80). An optional variable that is empty, unreadable or out of range is ignored: the default stays.
/// </summary>
public static class AiConfiguration
{
    public const string BaseUrlVariable = "PRESENTA_AI_BASE_URL";
    public const string ModelVariable = "PRESENTA_AI_MODEL";
    public const string ApiKeyVariable = "PRESENTA_AI_API_KEY";

    /// <summary><c>max_completion_tokens</c> (default) or <c>max_tokens</c>.</summary>
    public const string TokenParameterVariable = "PRESENTA_AI_TOKEN_PARAM";

    /// <summary>Token limit, 16 to 8000. Default 400.</summary>
    public const string MaxTokensVariable = "PRESENTA_AI_MAX_TOKENS";

    /// <summary>A number from 0 to 2: when set, <c>temperature</c> is sent. Unset: it is left out.</summary>
    public const string TemperatureVariable = "PRESENTA_AI_TEMPERATURE";

    /// <summary>A word such as <c>low</c> or <c>minimal</c>: when set, <c>reasoning_effort</c> is sent. Unset: it is left out.</summary>
    public const string ReasoningEffortVariable = "PRESENTA_AI_REASONING_EFFORT";

    /// <summary>Seconds to wait for one answer, 1 to 30. Default 8.</summary>
    public const string TimeoutVariable = "PRESENTA_AI_TIMEOUT_SECONDS";

    public static AiConfigurationResult FromEnvironment(Func<string, string?> read)
    {
        var baseUrl = read(BaseUrlVariable)?.Trim();
        var model = read(ModelVariable)?.Trim();
        var key = read(ApiKeyVariable)?.Trim();
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(model) || string.IsNullOrEmpty(key))
        {
            return new AiConfigurationResult(AiAvailability.MissingVariables, null);
        }

        var result = Create(baseUrl, model, key);
        if (result.Options is not { } basic)
        {
            return result;
        }

        var options = new AiEndpointOptions(basic.BaseUrl, basic.Model, key)
        {
            TokenLimitParameter = ParseTokenParameter(read(TokenParameterVariable)),
            MaxTokens = ParseInt(read(MaxTokensVariable), AiDefaults.MinTokensAllowed, AiDefaults.MaxTokensAllowed) ?? AiDefaults.MaxTokens,
            Temperature = ParseTemperature(read(TemperatureVariable)),
            ReasoningEffort = ParseEffort(read(ReasoningEffortVariable)),
            Timeout = ParseInt(read(TimeoutVariable), AiDefaults.MinTimeoutSeconds, AiDefaults.MaxTimeoutSeconds) is { } seconds
                ? TimeSpan.FromSeconds(seconds)
                : AiDefaults.Timeout,
        };
        return new AiConfigurationResult(AiAvailability.Available, options);
    }

    private static AiTokenLimitParameter ParseTokenParameter(string? value) =>
        string.Equals(value?.Trim(), "max_tokens", StringComparison.OrdinalIgnoreCase)
            ? AiTokenLimitParameter.MaxTokens
            : AiTokenLimitParameter.MaxCompletionTokens;

    private static int? ParseInt(string? value, int min, int max) =>
        int.TryParse(value?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
        && number >= min && number <= max
            ? number
            : null;

    private static double? ParseTemperature(string? value) =>
        double.TryParse(value?.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
        && number is >= 0 and <= 2
            ? number
            : null;

    private static string? ParseEffort(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > AiDefaults.MaxEffortLength)
        {
            return null;
        }

        // Letters, digits, dash and underscore only: the value goes into the request body as is.
        return text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? text.ToLowerInvariant() : null;
    }

    /// <summary>Validates the address: absolute, no credentials in it, and https unless the host is this computer.</summary>
    public static AiConfigurationResult Create(string baseUrl, string model, string apiKey)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return new AiConfigurationResult(AiAvailability.InvalidAddress, null);
        }

        if (uri.Scheme == "http" && !IsLoopback(uri))
        {
            return new AiConfigurationResult(AiAvailability.InsecureAddress, null);
        }

        return new AiConfigurationResult(AiAvailability.Available, new AiEndpointOptions(uri, model, apiKey));
    }

    private static bool IsLoopback(Uri uri)
    {
        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
    }
}
