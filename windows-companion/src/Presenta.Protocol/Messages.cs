namespace Presenta.Protocol;

/// <summary>Wire names of all message types in protocol v1.</summary>
public static class MessageTypes
{
    // Watch to companion
    public const string Hello = "hello";
    public const string SessionStart = "session.start";
    public const string SessionPause = "session.pause";
    public const string SessionResume = "session.resume";
    public const string SessionFinish = "session.finish";
    public const string SlideNext = "slide.next";
    public const string SlidePrevious = "slide.previous";
    public const string SlideSet = "slide.set";
    public const string AlertAcknowledge = "alert.acknowledge";
    public const string HistoryList = "history.list";
    public const string Ping = "ping";

    // Companion to watch
    public const string HelloAccepted = "hello.accepted";
    public const string HelloRejected = "hello.rejected";
    public const string StateUpdate = "state.update";
    public const string CommandResult = "command.result";
    public const string SessionSummary = "session.summary";
    public const string HistoryListResult = "history.list.result";
    public const string Error = "error";
    public const string Pong = "pong";
}

/// <summary>Constants of protocol v1.</summary>
public static class ProtocolInfo
{
    public const int Version = 1;
    public const int MaxIdLength = 64;
}

/// <summary>A protocol message: envelope id, type and a typed payload.</summary>
public abstract record ProtocolMessage(string Id)
{
    /// <summary>The wire message type.</summary>
    public abstract string Type { get; }

    internal abstract object PayloadObject { get; }
}

public abstract record ProtocolMessage<TPayload>(string Id, TPayload Payload) : ProtocolMessage(Id)
    where TPayload : notnull
{
    internal override object PayloadObject => Payload;
}

// ---- watch to companion ----

public sealed record HelloMessage(string Id, HelloPayload Payload) : ProtocolMessage<HelloPayload>(Id, Payload)
{
    public override string Type => MessageTypes.Hello;
}

public sealed record SessionStartMessage(string Id, SessionStartPayload Payload) : ProtocolMessage<SessionStartPayload>(Id, Payload)
{
    public override string Type => MessageTypes.SessionStart;
}

public sealed record SessionPauseMessage(string Id, EmptyPayload Payload) : ProtocolMessage<EmptyPayload>(Id, Payload)
{
    public override string Type => MessageTypes.SessionPause;
}

public sealed record SessionResumeMessage(string Id, EmptyPayload Payload) : ProtocolMessage<EmptyPayload>(Id, Payload)
{
    public override string Type => MessageTypes.SessionResume;
}

public sealed record SessionFinishMessage(string Id, EmptyPayload Payload) : ProtocolMessage<EmptyPayload>(Id, Payload)
{
    public override string Type => MessageTypes.SessionFinish;
}

public sealed record SlideNextMessage(string Id, EmptyPayload Payload) : ProtocolMessage<EmptyPayload>(Id, Payload)
{
    public override string Type => MessageTypes.SlideNext;
}

public sealed record SlidePreviousMessage(string Id, EmptyPayload Payload) : ProtocolMessage<EmptyPayload>(Id, Payload)
{
    public override string Type => MessageTypes.SlidePrevious;
}

public sealed record SlideSetMessage(string Id, SlideSetPayload Payload) : ProtocolMessage<SlideSetPayload>(Id, Payload)
{
    public override string Type => MessageTypes.SlideSet;
}

public sealed record AlertAcknowledgeMessage(string Id, EmptyPayload Payload) : ProtocolMessage<EmptyPayload>(Id, Payload)
{
    public override string Type => MessageTypes.AlertAcknowledge;
}

public sealed record HistoryListMessage(string Id, HistoryListPayload Payload) : ProtocolMessage<HistoryListPayload>(Id, Payload)
{
    public override string Type => MessageTypes.HistoryList;
}

public sealed record PingMessage(string Id, EmptyPayload Payload) : ProtocolMessage<EmptyPayload>(Id, Payload)
{
    public override string Type => MessageTypes.Ping;
}

// ---- companion to watch ----

public sealed record HelloAcceptedMessage(string Id, HelloAcceptedPayload Payload) : ProtocolMessage<HelloAcceptedPayload>(Id, Payload)
{
    public override string Type => MessageTypes.HelloAccepted;
}

public sealed record HelloRejectedMessage(string Id, HelloRejectedPayload Payload) : ProtocolMessage<HelloRejectedPayload>(Id, Payload)
{
    public override string Type => MessageTypes.HelloRejected;
}

public sealed record StateUpdateMessage(string Id, StatePayload Payload) : ProtocolMessage<StatePayload>(Id, Payload)
{
    public override string Type => MessageTypes.StateUpdate;
}

public sealed record CommandResultMessage(string Id, CommandResultPayload Payload) : ProtocolMessage<CommandResultPayload>(Id, Payload)
{
    public override string Type => MessageTypes.CommandResult;
}

public sealed record SessionSummaryMessage(string Id, HistoryItemPayload Payload) : ProtocolMessage<HistoryItemPayload>(Id, Payload)
{
    public override string Type => MessageTypes.SessionSummary;
}

public sealed record HistoryListResultMessage(string Id, HistoryListResultPayload Payload) : ProtocolMessage<HistoryListResultPayload>(Id, Payload)
{
    public override string Type => MessageTypes.HistoryListResult;
}

public sealed record ErrorMessage(string Id, ErrorPayload Payload) : ProtocolMessage<ErrorPayload>(Id, Payload)
{
    public override string Type => MessageTypes.Error;
}

public sealed record PongMessage(string Id, EmptyPayload Payload) : ProtocolMessage<EmptyPayload>(Id, Payload)
{
    public override string Type => MessageTypes.Pong;
}
