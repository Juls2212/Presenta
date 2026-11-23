using System.Text.Json.Nodes;
using Presenta.Protocol.Tests.Support;

namespace Presenta.Protocol.Tests;

/// <summary>The optional <c>freeText</c> of a recommendation (Phase 10): schema, parser and round trip agree.</summary>
public class FreeTextTests
{
    private static string Example(string name) => File.ReadAllText(Path.Combine(Contracts.ExamplesDir, name));

    private static string WithFreeText(string? value)
    {
        var node = JsonNode.Parse(Example("state.update--running-with-alert.json"))!;
        node["payload"]!["session"]!["recommendation"]!["freeText"] = value is null ? null : JsonValue.Create(value);
        return node.ToJsonString();
    }

    private static string? FreeTextOf(string json)
    {
        var parsed = ProtocolSerializer.Parse(json);
        Assert.True(parsed.Succeeded, parsed.Error?.Payload.Message);
        return ((StateUpdateMessage)parsed.Message!).Payload.Session!.Recommendation!.FreeText;
    }

    [Fact]
    public void A_text_is_parsed_kept_and_written_back_unchanged()
    {
        var json = Example("state.update--running-with-ai-text.json");

        var text = FreeTextOf(json);

        Assert.Equal("Acelera un poco, vas atrasado", text);
        Assert.Equal(text, FreeTextOf(ProtocolSerializer.Serialize(ProtocolSerializer.Parse(json).Message!)));
    }

    [Fact]
    public void Null_and_a_missing_text_are_both_read_as_no_text_and_null_is_written_as_null()
    {
        Assert.Null(FreeTextOf(WithFreeText(null)));
        var message = ProtocolSerializer.Parse(WithFreeText(null)).Message!;
        Assert.Contains("\"freeText\":null", ProtocolSerializer.Serialize(message), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(40, true)]
    [InlineData(80, true)]
    [InlineData(81, false)]
    [InlineData(500, false)]
    public void The_schema_allows_up_to_eighty_characters(int length, bool valid)
    {
        var json = WithFreeText(new string('a', length));

        Assert.Equal(valid, Contracts.IsValid("state.update", json));
        Assert.True(ProtocolSerializer.Parse(json).Succeeded); // the length is a contract limit for clients, not a parse error
    }

    [Fact]
    public void The_schema_still_accepts_null_and_rejects_other_types()
    {
        Assert.True(Contracts.IsValid("state.update", WithFreeText(null)));
        var number = Example("state.update--running-with-alert.json").Replace("\"freeText\": null", "\"freeText\": 12", StringComparison.Ordinal);
        Assert.False(Contracts.IsValid("state.update", number));
        Assert.False(ProtocolSerializer.Parse(number).Succeeded);
    }

    [Fact]
    public void Text_with_accents_and_inverted_marks_survives_the_round_trip()
    {
        var text = "\u00BFCaf\u00E9 \u00F1and\u00FA \u00FCber?";

        var again = FreeTextOf(ProtocolSerializer.Serialize(ProtocolSerializer.Parse(WithFreeText(text)).Message!));

        Assert.Equal(text, again);
    }
}
