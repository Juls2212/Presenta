using Presenta.Domain;
using Wire = Presenta.Protocol;

namespace Presenta.Server;

/// <summary>Converts domain results into protocol payloads. Enum names are identical on both sides (enforced by tests).</summary>
internal static class ProtocolMapper
{
    private static TTo Convert<TFrom, TTo>(TFrom value)
        where TFrom : struct, Enum
        where TTo : struct, Enum =>
        Enum.Parse<TTo>(value.ToString());

    public static Wire.ErrorCode ToErrorCode(SessionErrorCode error) => error switch
    {
        SessionErrorCode.InvalidState => Wire.ErrorCode.InvalidState,
        SessionErrorCode.SlideOutOfRange => Wire.ErrorCode.SlideOutOfRange,
        SessionErrorCode.InvalidConfiguration => Wire.ErrorCode.InvalidMessage,
        _ => Wire.ErrorCode.InternalError,
    };

    public static Wire.SessionSnapshotPayload ToPayload(SessionSnapshot snapshot) => new(
        Convert<SessionState, Wire.SessionState>(snapshot.State),
        snapshot.Name,
        snapshot.CurrentSlide,
        snapshot.TotalSlides,
        snapshot.PlannedDurationMs,
        snapshot.ElapsedMs,
        snapshot.RemainingMs,
        snapshot.IsRunning,
        snapshot.ProgressRatio,
        Convert<PaceStatus, Wire.PaceStatus>(snapshot.PaceStatus),
        snapshot.SlidesRemaining,
        snapshot.AverageSlidesPerMinute,
        snapshot.FinalState is { } final ? Convert<FinalState, Wire.FinalState>(final) : null,
        snapshot.ActiveAlert is { } alert ? ToPayload(alert) : null,
        snapshot.Recommendation is { } recommendation ? ToPayload(recommendation) : null);

    public static Wire.AlertPayload ToPayload(Alert alert) => new(
        Convert<AlertKind, Wire.AlertKind>(alert.Kind),
        Convert<AlertSeverity, Wire.AlertSeverity>(alert.Severity),
        alert.RaisedAtElapsedMs,
        alert.RemainingMs,
        alert.SlidesRemaining,
        alert.SlideDeviation);

    public static Wire.RecommendationPayload ToPayload(Recommendation recommendation) => new(
        Convert<RecommendationCode, Wire.RecommendationCode>(recommendation.Code),
        recommendation.SlidesRemaining,
        recommendation.TimeRemainingMs,
        recommendation.FreeText);

    public static Wire.HistoryItemPayload ToPayload(SessionSummary summary) => new(
        summary.Name,
        summary.FinishedAtUtc,
        summary.PlannedDurationMs,
        summary.ActualDurationMs,
        summary.PlannedSlides,
        summary.RegisteredSlides,
        summary.AverageSlidesPerMinute,
        Convert<FinalState, Wire.FinalState>(summary.FinalState),
        summary.EndedWithinPlannedTime);
}
