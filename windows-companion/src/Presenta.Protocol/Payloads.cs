using System.Text.Json.Serialization;

namespace Presenta.Protocol;

// Wire models for protocol v1. Property names are camelCase on the wire. Constructor parameters without a
// default value are required on the wire (a nullable one must be present, possibly as null). Optional
// properties have a default and are omitted when null.

/// <summary>Payload of messages that carry no data (<c>session.pause</c>, <c>ping</c>, ...).</summary>
public sealed record EmptyPayload;

public sealed record HelloPayload(string PairingCode, int ProtocolVersion, string ClientName);

public sealed record HelloAcceptedPayload(int ProtocolVersion, StatePayload State);

public sealed record HelloRejectedPayload(
    ErrorCode Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? RetryAfterMs = null);

public sealed record SessionStartPayload(
    int PlannedSlides,
    long PlannedDurationMs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null);

public sealed record SlideSetPayload(int Slide);

public sealed record HistoryListPayload(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Limit = null);

/// <summary>Full companion state. <see cref="Session"/> is null when there is no active session.</summary>
public sealed record StatePayload(DateTimeOffset ServerTimeUtc, SessionSnapshotPayload? Session);

public sealed record SessionSnapshotPayload(
    SessionState State,
    string Name,
    int CurrentSlide,
    int TotalSlides,
    long PlannedDurationMs,
    long ElapsedMs,
    long RemainingMs,
    bool Running,
    double ProgressRatio,
    PaceStatus PaceStatus,
    int SlidesRemaining,
    double AverageSlidesPerMinute,
    FinalState? FinalState,
    AlertPayload? ActiveAlert,
    RecommendationPayload? Recommendation);

public sealed record AlertPayload(
    AlertKind Kind,
    AlertSeverity Severity,
    long RaisedAtElapsedMs,
    long RemainingMs,
    int SlidesRemaining,
    int SlideDeviation);

public sealed record RecommendationPayload(
    RecommendationCode Code,
    int SlidesRemaining,
    long TimeRemainingMs,
    string? FreeText);

public sealed record CommandResultPayload(string CorrelationId, bool Ok, ErrorCode? ErrorCode);

/// <summary>A finished session. Used by <c>session.summary</c> and <c>history.list.result</c>.</summary>
public sealed record HistoryItemPayload(
    string Name,
    DateTimeOffset FinishedAtUtc,
    long PlannedDurationMs,
    long ActualDurationMs,
    int PlannedSlides,
    int RegisteredSlides,
    double AverageSlidesPerMinute,
    FinalState FinalState,
    bool EndedWithinPlannedTime);

public sealed record HistoryListResultPayload(IReadOnlyList<HistoryItemPayload> Items);

public sealed record ErrorPayload(
    ErrorCode Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CorrelationId = null);
