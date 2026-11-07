namespace Presenta.Domain;

/// <summary>
/// Immutable view of a session at one instant. Durations are integer milliseconds. The watch interpolates
/// between snapshots using <see cref="ServerTimeUtc"/>, <see cref="ElapsedMs"/> and <see cref="IsRunning"/>.
/// </summary>
public sealed record SessionSnapshot(
    SessionState State,
    string Name,
    int CurrentSlide,
    int TotalSlides,
    long PlannedDurationMs,
    long ElapsedMs,
    long RemainingMs,
    bool IsRunning,
    DateTimeOffset ServerTimeUtc,
    double ProgressRatio,
    PaceStatus PaceStatus,
    int SlidesRemaining,
    double AverageSlidesPerMinute,
    FinalState? FinalState,
    Alert? ActiveAlert,
    Recommendation? Recommendation);

/// <summary>Immutable result of a finished session. Same fields as a history item.</summary>
public sealed record SessionSummary(
    string Name,
    DateTimeOffset FinishedAtUtc,
    long PlannedDurationMs,
    long ActualDurationMs,
    int PlannedSlides,
    int RegisteredSlides,
    double AverageSlidesPerMinute,
    FinalState FinalState,
    bool EndedWithinPlannedTime);
