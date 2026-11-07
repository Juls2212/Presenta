namespace Presenta.Domain;

/// <summary>Hard limits for session configuration. The JSON Schemas in shared-contracts use the same numbers.</summary>
public static class SessionLimits
{
    public const int MinSlides = 1;
    public const int MaxSlides = 999;
    public const long MinPlannedDurationMs = 1_000;
    public const long MaxPlannedDurationMs = 43_200_000; // 12 hours
    public const int MaxNameLength = 100;

    /// <summary>Neutral fallback used when a session is started without a name.</summary>
    public const string DefaultSessionName = "Presentation";
}
