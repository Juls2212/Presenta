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

    /// <summary>Maximum time to wait for the provider. About four seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>Hard cap on the answer length in tokens: a tip of 40 characters needs far fewer.</summary>
    public int MaxTokens { get; init; } = 32;

    public double Temperature { get; init; } = 0.2;

    /// <summary>The key, for the Authorization header only.</summary>
    internal string ApiKey => _apiKey;

    public override string ToString() => $"AiEndpointOptions {{ BaseUrl = {BaseUrl.Scheme}://{BaseUrl.Authority}, Model = {Model}, ApiKey = *** }}";
}

/// <summary>Result of reading the three environment variables.</summary>
public sealed record AiConfigurationResult(AiAvailability Availability, AiEndpointOptions? Options)
{
    public bool IsAvailable => Availability == AiAvailability.Available;
}

/// <summary>Reads <c>PRESENTA_AI_BASE_URL</c>, <c>PRESENTA_AI_MODEL</c> and <c>PRESENTA_AI_API_KEY</c> (ADR-73).</summary>
public static class AiConfiguration
{
    public const string BaseUrlVariable = "PRESENTA_AI_BASE_URL";
    public const string ModelVariable = "PRESENTA_AI_MODEL";
    public const string ApiKeyVariable = "PRESENTA_AI_API_KEY";

    public static AiConfigurationResult FromEnvironment(Func<string, string?> read)
    {
        var baseUrl = read(BaseUrlVariable)?.Trim();
        var model = read(ModelVariable)?.Trim();
        var key = read(ApiKeyVariable)?.Trim();
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(model) || string.IsNullOrEmpty(key))
        {
            return new AiConfigurationResult(AiAvailability.MissingVariables, null);
        }

        return Create(baseUrl, model, key);
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
