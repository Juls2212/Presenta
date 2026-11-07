using Presenta.Protocol.Tests.Support;

namespace Presenta.Protocol.Tests;

public class StrictParsingTests
{
    private const string PingWith = """{ "version": 1, "type": "ping", "id": "m-1", "payload": {} }""";

    [Fact]
    public void Unknown_extra_properties_are_tolerated_in_the_envelope_and_in_payloads()
    {
        const string json = """
            { "version": 1, "type": "slide.set", "id": "m-1", "futureField": [1, 2],
              "payload": { "slide": 4, "reason": "manual", "nested": { "a": 1 } } }
            """;

        var parsed = ProtocolSerializer.Parse(json);

        Assert.True(parsed.Succeeded);
        Assert.Equal(4, ((SlideSetMessage)parsed.Message!).Payload.Slide);
    }

    [Fact]
    public void Unknown_extra_properties_are_tolerated_inside_nested_state_objects()
    {
        const string json = """
            { "version": 1, "type": "state.update", "id": "s-1",
              "payload": { "serverTimeUtc": "2026-10-31T15:30:00.000Z", "session": null, "extra": true } }
            """;

        Assert.True(ProtocolSerializer.Parse(json).Succeeded);
    }

    [Fact]
    public void Unknown_message_type_returns_an_error_without_throwing()
    {
        const string json = """{ "version": 1, "type": "slide.teleport", "id": "m-42", "payload": {} }""";

        var parsed = ProtocolSerializer.Parse(json);

        Assert.False(parsed.Succeeded);
        Assert.Null(parsed.Message);
        Assert.NotNull(parsed.Error);
        Assert.Equal(ErrorCode.InvalidMessage, parsed.Error.Payload.Code);
        Assert.Equal("m-42", parsed.Error.Payload.CorrelationId);
        Assert.Contains("slide.teleport", parsed.Error.Payload.Message);
    }

    [Fact]
    public void Error_reply_is_itself_a_valid_protocol_message()
    {
        var parsed = ProtocolSerializer.Parse("""{ "version": 1, "type": "nope", "id": "m-1", "payload": {} }""");

        var json = ProtocolSerializer.Serialize(parsed.Error!);

        Assert.True(Contracts.IsValid(MessageTypes.Error, json), json);
    }

    [Fact]
    public void Error_ids_come_from_the_supplied_factory()
    {
        var parsed = ProtocolSerializer.Parse("not json", () => "err-fixed");

        Assert.Equal("err-fixed", parsed.Error!.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("\"text\"")]
    public void Malformed_input_returns_invalid_message_without_throwing(string json)
    {
        var parsed = ProtocolSerializer.Parse(json);

        Assert.False(parsed.Succeeded);
        Assert.Equal(ErrorCode.InvalidMessage, parsed.Error!.Payload.Code);
    }

    [Fact]
    public void A_number_sent_as_a_string_is_rejected()
    {
        const string json = """{ "version": 1, "type": "slide.set", "id": "m-1", "payload": { "slide": "4" } }""";

        var parsed = ProtocolSerializer.Parse(json);

        Assert.False(parsed.Succeeded);
        Assert.Equal(ErrorCode.InvalidMessage, parsed.Error!.Payload.Code);
    }

    [Fact]
    public void A_version_sent_as_a_string_is_rejected()
    {
        var parsed = ProtocolSerializer.Parse("""{ "version": "1", "type": "ping", "id": "m-1", "payload": {} }""");

        Assert.Equal(ErrorCode.InvalidMessage, parsed.Error!.Payload.Code);
    }

    [Fact]
    public void A_non_integer_number_is_rejected_for_an_integer_property()
    {
        const string json = """{ "version": 1, "type": "slide.set", "id": "m-1", "payload": { "slide": 4.5 } }""";

        Assert.False(ProtocolSerializer.Parse(json).Succeeded);
    }

    [Theory]
    [InlineData("2026-10-31T15:30:00")]
    [InlineData("2026-10-31T15:30:00+00:00")]
    [InlineData("2026-10-31T15:30:00-05:00")]
    [InlineData("2026-10-31 15:30:00Z")]
    [InlineData("2026-10-31")]
    [InlineData("not a date")]
    public void Timestamps_that_are_not_utc_with_a_trailing_z_are_rejected(string timestamp)
    {
        var json = $$"""{ "version": 1, "type": "state.update", "id": "s-1", "payload": { "serverTimeUtc": "{{timestamp}}", "session": null } }""";

        var parsed = ProtocolSerializer.Parse(json);

        Assert.False(parsed.Succeeded);
        Assert.Equal(ErrorCode.InvalidMessage, parsed.Error!.Payload.Code);
    }

    [Theory]
    [InlineData("2026-10-31T15:30:00Z")]
    [InlineData("2026-10-31T15:30:00.5Z")]
    [InlineData("2026-10-31T15:30:00.1234567Z")]
    public void Utc_timestamps_with_or_without_fractions_are_accepted(string timestamp)
    {
        var json = $$"""{ "version": 1, "type": "state.update", "id": "s-1", "payload": { "serverTimeUtc": "{{timestamp}}", "session": null } }""";

        var parsed = ProtocolSerializer.Parse(json);

        Assert.True(parsed.Succeeded);
        Assert.Equal(TimeSpan.Zero, ((StateUpdateMessage)parsed.Message!).Payload.ServerTimeUtc.Offset);
    }

    [Fact]
    public void A_timestamp_sent_as_a_number_is_rejected()
    {
        const string json = """{ "version": 1, "type": "state.update", "id": "s-1", "payload": { "serverTimeUtc": 1761924600, "session": null } }""";

        Assert.False(ProtocolSerializer.Parse(json).Succeeded);
    }

    [Fact]
    public void Enum_values_must_be_known_strings()
    {
        var unknown = ProtocolSerializer.Parse("""{ "version": 1, "type": "error", "id": "s-1", "payload": { "code": "nope", "message": "x" } }""");
        var number = ProtocolSerializer.Parse("""{ "version": 1, "type": "error", "id": "s-1", "payload": { "code": 3, "message": "x" } }""");

        Assert.False(unknown.Succeeded);
        Assert.False(number.Succeeded);
    }

    [Fact]
    public void Property_names_are_case_sensitive()
    {
        const string json = """{ "version": 1, "type": "slide.set", "id": "m-1", "payload": { "Slide": 4 } }""";

        Assert.False(ProtocolSerializer.Parse(json).Succeeded);
    }

    [Fact]
    public void A_missing_required_payload_property_is_rejected()
    {
        const string json = """{ "version": 1, "type": "hello", "id": "m-1", "payload": { "protocolVersion": 1, "clientName": "x" } }""";

        Assert.Equal(ErrorCode.InvalidMessage, ProtocolSerializer.Parse(json).Error!.Payload.Code);
    }

    [Fact]
    public void A_required_nullable_property_must_still_be_present()
    {
        const string json = """{ "version": 1, "type": "command.result", "id": "s-1", "payload": { "correlationId": "m-1", "ok": true } }""";

        Assert.False(ProtocolSerializer.Parse(json).Succeeded);
    }

    [Fact]
    public void A_null_for_a_non_nullable_property_is_rejected()
    {
        const string json = """{ "version": 1, "type": "hello", "id": "m-1", "payload": { "pairingCode": null, "protocolVersion": 1, "clientName": "x" } }""";

        Assert.False(ProtocolSerializer.Parse(json).Succeeded);
    }

    [Fact]
    public void An_unsupported_version_is_reported_with_its_own_error_code()
    {
        var parsed = ProtocolSerializer.Parse("""{ "version": 2, "type": "ping", "id": "m-1", "payload": {} }""");

        Assert.Equal(ErrorCode.UnsupportedVersion, parsed.Error!.Payload.Code);
        Assert.Equal("m-1", parsed.Error.Payload.CorrelationId);
    }

    [Theory]
    [InlineData("""{ "type": "ping", "id": "m-1", "payload": {} }""")]
    [InlineData("""{ "version": 1, "id": "m-1", "payload": {} }""")]
    [InlineData("""{ "version": 1, "type": "ping", "payload": {} }""")]
    [InlineData("""{ "version": 1, "type": "ping", "id": "m-1" }""")]
    [InlineData("""{ "version": 1, "type": "", "id": "m-1", "payload": {} }""")]
    [InlineData("""{ "version": 1, "type": "ping", "id": "", "payload": {} }""")]
    [InlineData("""{ "version": 1, "type": "ping", "id": "m-1", "payload": null }""")]
    [InlineData("""{ "version": 1, "type": "ping", "id": "m-1", "payload": "x" }""")]
    [InlineData("""{ "version": 1, "type": 5, "id": "m-1", "payload": {} }""")]
    public void Envelope_violations_return_invalid_message(string json)
    {
        var parsed = ProtocolSerializer.Parse(json);

        Assert.False(parsed.Succeeded);
        Assert.Equal(ErrorCode.InvalidMessage, parsed.Error!.Payload.Code);
    }

    [Fact]
    public void An_id_longer_than_the_limit_is_rejected()
    {
        var json = $$"""{ "version": 1, "type": "ping", "id": "{{new string('x', ProtocolInfo.MaxIdLength + 1)}}", "payload": {} }""";

        Assert.False(ProtocolSerializer.Parse(json).Succeeded);
    }

    [Fact]
    public void An_id_at_the_limit_is_accepted()
    {
        var json = $$"""{ "version": 1, "type": "ping", "id": "{{new string('x', ProtocolInfo.MaxIdLength)}}", "payload": {} }""";

        Assert.True(ProtocolSerializer.Parse(json).Succeeded);
    }

    [Fact]
    public void A_valid_minimal_message_is_accepted()
    {
        Assert.IsType<PingMessage>(ProtocolSerializer.Parse(PingWith).Message);
    }

    [Fact]
    public void Excessively_deep_json_is_rejected_without_throwing()
    {
        var deep = new string('[', 100) + new string(']', 100);

        var parsed = ProtocolSerializer.Parse(deep);

        Assert.False(parsed.Succeeded);
    }
}
