namespace Presenta.Domain;

/// <summary>
/// Tunable thresholds for pace calculation and alerts. All defaults are the documented V1 values.
/// </summary>
public sealed record SessionOptions
{
    /// <summary>Pace tolerance as a fraction of total slides (default 10%).</summary>
    public double PaceToleranceFraction { get; init; } = 0.10;

    /// <summary>Lower bound of the pace tolerance in slides (default 1).</summary>
    public int MinimumPaceTolerance { get; init; } = 1;

    /// <summary>Upper bound of the warm-up period (default 60 s).</summary>
    public TimeSpan WarmUpMax { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Warm-up as a fraction of planned time (default 10%). The shorter of the two bounds applies.</summary>
    public double WarmUpFraction { get; init; } = 0.10;

    /// <summary>Upper bound of the "time running out" threshold (default 5 min).</summary>
    public TimeSpan TimeRunningOutMax { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>"Time running out" threshold as a fraction of planned time (default 30%). The shorter bound applies.</summary>
    public double TimeRunningOutFraction { get; init; } = 0.30;

    /// <summary>Requested "time critical" threshold (default 1 min); capped at the running-out threshold.</summary>
    public TimeSpan TimeCritical { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Minimum time between repeated pace alerts (default 2 min).</summary>
    public TimeSpan PaceAlertCooldown { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Average pace is reported as 0 below this elapsed time (default 5 s).</summary>
    public TimeSpan MinimumElapsedForAveragePace { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Pace tolerance in slides: max(minimum, round(fraction of total slides)).</summary>
    public int GetPaceTolerance(int totalSlides) =>
        Math.Max(MinimumPaceTolerance, (int)Math.Round(PaceToleranceFraction * totalSlides, MidpointRounding.AwayFromZero));

    /// <summary>Warm-up duration: min(WarmUpMax, fraction of planned time).</summary>
    public TimeSpan GetWarmUp(TimeSpan planned) =>
        Min(WarmUpMax, Fraction(planned, WarmUpFraction));

    /// <summary>Remaining-time threshold for the "time running out" alert: min(max, fraction of planned time).</summary>
    public TimeSpan GetTimeRunningOutThreshold(TimeSpan planned) =>
        Min(TimeRunningOutMax, Fraction(planned, TimeRunningOutFraction));

    /// <summary>Remaining-time threshold for the "time critical" alert, capped at the running-out threshold.</summary>
    public TimeSpan GetTimeCriticalThreshold(TimeSpan planned) =>
        Min(TimeCritical, GetTimeRunningOutThreshold(planned));

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a <= b ? a : b;

    // Rounded (not truncated) so that fractions such as 0.3 do not lose a tick to floating-point error.
    private static TimeSpan Fraction(TimeSpan value, double fraction) =>
        TimeSpan.FromTicks((long)Math.Round(value.Ticks * fraction));
}
