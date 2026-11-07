namespace Presenta.Protocol.Tests.Support;

/// <summary>One fully populated message for every message type of protocol v1.</summary>
internal static class SampleMessages
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 15, 8, 32, 123, TimeSpan.Zero);

    public static readonly SessionSnapshotPayload Snapshot = new(
        State: SessionState.Running,
        Name: "Demo session",
        CurrentSlide: 7,
        TotalSlides: 20,
        PlannedDurationMs: 900_000,
        ElapsedMs: 512_000,
        RemainingMs: 388_000,
        Running: true,
        ProgressRatio: 0.35,
        PaceStatus: PaceStatus.Behind,
        SlidesRemaining: 13,
        AverageSlidesPerMinute: 0.82,
        FinalState: null,
        ActiveAlert: new AlertPayload(AlertKind.BehindSchedule, AlertSeverity.Warning, 480_000, 420_000, 13, -3),
        Recommendation: new RecommendationPayload(RecommendationCode.SpeedUpSlightly, 13, 388_000, null));

    public static readonly HistoryItemPayload HistoryItem = new(
        Name: "Demo session",
        FinishedAtUtc: Now,
        PlannedDurationMs: 900_000,
        ActualDurationMs: 872_000,
        PlannedSlides: 20,
        RegisteredSlides: 20,
        AverageSlidesPerMinute: 1.376,
        FinalState: FinalState.FinishedManually,
        EndedWithinPlannedTime: true);

    public static IReadOnlyDictionary<string, ProtocolMessage> ByType { get; } = Build();

    private static Dictionary<string, ProtocolMessage> Build()
    {
        var all = new ProtocolMessage[]
        {
            new HelloMessage("m-1", new HelloPayload("482913", 1, "Wear OS emulator")),
            new SessionStartMessage("m-2", new SessionStartPayload(20, 900_000, "Demo session")),
            new SessionPauseMessage("m-3", new EmptyPayload()),
            new SessionResumeMessage("m-4", new EmptyPayload()),
            new SessionFinishMessage("m-5", new EmptyPayload()),
            new SlideNextMessage("m-6", new EmptyPayload()),
            new SlidePreviousMessage("m-7", new EmptyPayload()),
            new SlideSetMessage("m-8", new SlideSetPayload(12)),
            new AlertAcknowledgeMessage("m-9", new EmptyPayload()),
            new HistoryListMessage("m-10", new HistoryListPayload(20)),
            new PingMessage("m-11", new EmptyPayload()),
            new HelloAcceptedMessage("s-1", new HelloAcceptedPayload(1, new StatePayload(Now, Snapshot))),
            new HelloRejectedMessage("s-2", new HelloRejectedPayload(ErrorCode.PairingLockedOut, 60_000)),
            new StateUpdateMessage("s-3", new StatePayload(Now, Snapshot)),
            new CommandResultMessage("s-4", new CommandResultPayload("m-8", false, ErrorCode.SlideOutOfRange)),
            new SessionSummaryMessage("s-5", HistoryItem),
            new HistoryListResultMessage("s-6", new HistoryListResultPayload([HistoryItem, HistoryItem with { Name = "Rehearsal" }])),
            new ErrorMessage("s-7", new ErrorPayload(ErrorCode.InvalidState, "Not allowed in this state.", "m-3")),
            new PongMessage("s-8", new EmptyPayload()),
        };

        return all.ToDictionary(m => m.Type, StringComparer.Ordinal);
    }
}
