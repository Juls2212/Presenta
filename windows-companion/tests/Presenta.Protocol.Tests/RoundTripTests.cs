using System.Text.Json.Nodes;
using Presenta.Protocol.Tests.Support;

namespace Presenta.Protocol.Tests;

public class RoundTripTests
{
    public static IEnumerable<object[]> AllMessageTypes() =>
        MessageTypeList.All.Select(type => new object[] { type });

    [Fact]
    public void Samples_cover_every_message_type_known_to_the_serializer()
    {
        Assert.Equal(
            ProtocolSerializer.KnownTypes.Order(StringComparer.Ordinal),
            SampleMessages.ByType.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(19, SampleMessages.ByType.Count);
    }

    [Fact]
    public void Message_type_constants_match_the_serializer_registry()
    {
        Assert.Equal(
            MessageTypeList.All.Order(StringComparer.Ordinal),
            ProtocolSerializer.KnownTypes.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(AllMessageTypes))]
    public void Message_survives_serialize_parse_serialize(string type)
    {
        var message = SampleMessages.ByType[type];

        var json = ProtocolSerializer.Serialize(message);
        var parsed = ProtocolSerializer.Parse(json);

        Assert.True(parsed.Succeeded, parsed.Error?.Payload.Message);
        Assert.NotNull(parsed.Message);
        Assert.Equal(message.GetType(), parsed.Message.GetType());
        Assert.Equal(message.Id, parsed.Message.Id);
        Assert.Equal(type, parsed.Message.Type);
        Assert.Equal(json, ProtocolSerializer.Serialize(parsed.Message));
    }

    [Theory]
    [MemberData(nameof(AllMessageTypes))]
    public void Serialized_message_validates_against_its_schema(string type)
    {
        var json = ProtocolSerializer.Serialize(SampleMessages.ByType[type]);

        Assert.True(Contracts.IsValid(type, json), json);
    }

    [Fact]
    public void Messages_without_list_payloads_are_equal_after_a_round_trip()
    {
        var message = SampleMessages.ByType[MessageTypes.StateUpdate];

        var parsed = ProtocolSerializer.Parse(ProtocolSerializer.Serialize(message));

        Assert.Equal(message, parsed.Message);
    }

    [Fact]
    public void History_list_result_keeps_all_items_in_order()
    {
        var message = (HistoryListResultMessage)SampleMessages.ByType[MessageTypes.HistoryListResult];

        var parsed = (HistoryListResultMessage)ProtocolSerializer.Parse(ProtocolSerializer.Serialize(message)).Message!;

        Assert.Equal(["Demo session", "Rehearsal"], parsed.Payload.Items.Select(i => i.Name));
    }

    [Fact]
    public void Wire_names_are_camel_case_and_enums_are_lower_camel_case_strings()
    {
        var json = ProtocolSerializer.Serialize(SampleMessages.ByType[MessageTypes.StateUpdate]);

        var payload = JsonNode.Parse(json)!["payload"]!;
        var session = payload["session"]!;
        Assert.Equal("behind", (string?)session["paceStatus"]);
        Assert.Equal("behindSchedule", (string?)session["activeAlert"]!["kind"]);
        Assert.Equal("speedUpSlightly", (string?)session["recommendation"]!["code"]);
        Assert.Equal(512_000, (long?)session["elapsedMs"]);
        Assert.Equal(true, (bool?)session["running"]);
    }

    [Fact]
    public void No_active_session_is_an_explicit_null_session()
    {
        var message = new StateUpdateMessage("s-1", new StatePayload(new DateTimeOffset(2026, 10, 31, 15, 30, 0, TimeSpan.Zero), null));

        var json = ProtocolSerializer.Serialize(message);

        Assert.Contains("\"session\":null", json);
        Assert.True(Contracts.IsValid(MessageTypes.StateUpdate, json));
        var parsed = (StateUpdateMessage)ProtocolSerializer.Parse(json).Message!;
        Assert.Null(parsed.Payload.Session);
    }

    [Fact]
    public void Absent_optional_properties_are_omitted()
    {
        var start = ProtocolSerializer.Serialize(new SessionStartMessage("m-1", new SessionStartPayload(10, 600_000)));
        var list = ProtocolSerializer.Serialize(new HistoryListMessage("m-2", new HistoryListPayload()));
        var rejected = ProtocolSerializer.Serialize(new HelloRejectedMessage("s-1", new HelloRejectedPayload(ErrorCode.InvalidPairingCode)));
        var error = ProtocolSerializer.Serialize(new ErrorMessage("s-2", new ErrorPayload(ErrorCode.InternalError, "x")));

        Assert.DoesNotContain("name", start);
        Assert.DoesNotContain("limit", list);
        Assert.DoesNotContain("retryAfterMs", rejected);
        Assert.DoesNotContain("correlationId", error);
        Assert.True(Contracts.IsValid(MessageTypes.SessionStart, start));
        Assert.True(Contracts.IsValid(MessageTypes.HistoryList, list));
        Assert.True(Contracts.IsValid(MessageTypes.HelloRejected, rejected));
        Assert.True(Contracts.IsValid(MessageTypes.Error, error));
    }

    [Fact]
    public void Timestamps_are_written_in_utc_with_milliseconds_and_a_trailing_z()
    {
        var local = new DateTimeOffset(2026, 10, 31, 10, 8, 32, 123, TimeSpan.FromHours(-5));
        var message = new SessionSummaryMessage("s-1", SampleMessages.HistoryItem with { FinishedAtUtc = local });

        var json = ProtocolSerializer.Serialize(message);

        Assert.Contains("\"finishedAtUtc\":\"2026-10-31T15:08:32.123Z\"", json);
    }

    [Fact]
    public void Non_ascii_text_is_written_readably_and_round_trips()
    {
        var name = "Demo ü 中"; // escapes keep this source file ASCII-only
        var json = ProtocolSerializer.Serialize(new SessionStartMessage("m-1", new SessionStartPayload(10, 600_000, name)));

        Assert.Contains(name, json);
        var parsed = (SessionStartMessage)ProtocolSerializer.Parse(json).Message!;
        Assert.Equal(name, parsed.Payload.Name);
    }
}

internal static class MessageTypeList
{
    public static readonly string[] All =
    [
        MessageTypes.Hello, MessageTypes.SessionStart, MessageTypes.SessionPause, MessageTypes.SessionResume,
        MessageTypes.SessionFinish, MessageTypes.SlideNext, MessageTypes.SlidePrevious, MessageTypes.SlideSet,
        MessageTypes.AlertAcknowledge, MessageTypes.HistoryList, MessageTypes.Ping, MessageTypes.HelloAccepted,
        MessageTypes.HelloRejected, MessageTypes.StateUpdate, MessageTypes.CommandResult, MessageTypes.SessionSummary,
        MessageTypes.HistoryListResult, MessageTypes.Error, MessageTypes.Pong,
    ];
}
