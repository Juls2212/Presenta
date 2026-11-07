namespace Presenta.Domain;

/// <summary>Validated configuration of a session. Create it with <see cref="Create"/>.</summary>
public sealed record SessionConfiguration
{
    private SessionConfiguration(string name, int plannedSlides, TimeSpan plannedDuration)
    {
        Name = name;
        PlannedSlides = plannedSlides;
        PlannedDuration = plannedDuration;
    }

    public string Name { get; }

    public int PlannedSlides { get; }

    public TimeSpan PlannedDuration { get; }

    /// <summary>
    /// Validates the inputs. A null or blank name is replaced by <see cref="SessionLimits.DefaultSessionName"/>.
    /// Any limit violation returns <see cref="SessionErrorCode.InvalidConfiguration"/>.
    /// </summary>
    public static Result<SessionConfiguration> Create(int plannedSlides, TimeSpan plannedDuration, string? name = null)
    {
        if (plannedSlides < SessionLimits.MinSlides || plannedSlides > SessionLimits.MaxSlides)
        {
            return Result<SessionConfiguration>.Failure(SessionErrorCode.InvalidConfiguration);
        }

        if (plannedDuration.TotalMilliseconds < SessionLimits.MinPlannedDurationMs
            || plannedDuration.TotalMilliseconds > SessionLimits.MaxPlannedDurationMs)
        {
            return Result<SessionConfiguration>.Failure(SessionErrorCode.InvalidConfiguration);
        }

        var trimmed = string.IsNullOrWhiteSpace(name) ? SessionLimits.DefaultSessionName : name.Trim();
        if (trimmed.Length > SessionLimits.MaxNameLength)
        {
            return Result<SessionConfiguration>.Failure(SessionErrorCode.InvalidConfiguration);
        }

        return Result<SessionConfiguration>.Success(new SessionConfiguration(trimmed, plannedSlides, plannedDuration));
    }
}
