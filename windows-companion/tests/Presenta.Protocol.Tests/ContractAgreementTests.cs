using System.Text.Json;
using System.Text.Json.Nodes;
using Presenta.Protocol.Tests.Support;

namespace Presenta.Protocol.Tests;

/// <summary>Checks that C# enums, the JSON Schemas, protocol-v1.md and the domain agree.</summary>
public class ContractAgreementTests
{
    private static IEnumerable<string> WireNames<TEnum>() where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>().Select(JsonNamingPolicy.CamelCase.ConvertName);

    private static IEnumerable<string> SchemaEnum(string definition) =>
        Contracts.ReadSchemaJson("common.schema.json")["$defs"]![definition]!["enum"]!
            .AsArray().Select(n => n!.GetValue<string>());

    [Fact]
    public void Error_code_enum_matches_the_schema_enum_exactly()
    {
        Assert.Equal(
            SchemaEnum("errorCode").Order(StringComparer.Ordinal),
            WireNames<ErrorCode>().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Error_code_enum_matches_the_error_code_table_in_protocol_v1_md_exactly()
    {
        var inDocument = new List<string>();
        var inSection = false;
        foreach (var line in File.ReadAllLines(Contracts.ProtocolDocument))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inSection = line.Trim() == "## Error codes";
                continue;
            }

            if (inSection && line.StartsWith("| `", StringComparison.Ordinal))
            {
                var end = line.IndexOf('`', 3);
                inDocument.Add(line[3..end]);
            }
        }

        Assert.Equal(
            WireNames<ErrorCode>().Order(StringComparer.Ordinal),
            inDocument.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Other_enums_match_their_schema_definitions_exactly()
    {
        Assert.Equal(SchemaEnum("sessionState").Order(), WireNames<SessionState>().Order());
        Assert.Equal(SchemaEnum("finalState").Order(), WireNames<FinalState>().Order());
        Assert.Equal(SchemaEnum("paceStatus").Order(), WireNames<PaceStatus>().Order());
        Assert.Equal(SchemaEnum("alertKind").Order(), WireNames<AlertKind>().Order());
        Assert.Equal(SchemaEnum("alertSeverity").Order(), WireNames<AlertSeverity>().Order());
        Assert.Equal(SchemaEnum("recommendationCode").Order(), WireNames<RecommendationCode>().Order());
    }

    [Fact]
    public void Wire_enums_match_the_domain_enums_by_name()
    {
        Assert.Equal(Enum.GetNames<Domain.SessionState>(), Enum.GetNames<SessionState>());
        Assert.Equal(Enum.GetNames<Domain.FinalState>(), Enum.GetNames<FinalState>());
        Assert.Equal(Enum.GetNames<Domain.PaceStatus>(), Enum.GetNames<PaceStatus>());
        Assert.Equal(Enum.GetNames<Domain.AlertKind>(), Enum.GetNames<AlertKind>());
        Assert.Equal(Enum.GetNames<Domain.AlertSeverity>(), Enum.GetNames<AlertSeverity>());
        Assert.Equal(Enum.GetNames<Domain.RecommendationCode>(), Enum.GetNames<RecommendationCode>());
    }

    [Fact]
    public void Every_domain_error_code_maps_to_a_protocol_error_code()
    {
        var mapping = new Dictionary<Domain.SessionErrorCode, ErrorCode>
        {
            [Domain.SessionErrorCode.InvalidState] = ErrorCode.InvalidState,
            [Domain.SessionErrorCode.SlideOutOfRange] = ErrorCode.SlideOutOfRange,
            [Domain.SessionErrorCode.InvalidConfiguration] = ErrorCode.InvalidMessage,
        };

        foreach (var code in Enum.GetValues<Domain.SessionErrorCode>())
        {
            Assert.True(mapping.ContainsKey(code), $"Domain error {code} has no protocol mapping.");
        }
    }

    [Fact]
    public void Session_limits_in_the_domain_match_the_schemas()
    {
        var common = Contracts.ReadSchemaJson("common.schema.json")["$defs"]!;
        var start = Contracts.ReadSchemaJson("session.start.schema.json")["properties"]!["payload"]!["properties"]!;
        var historyName = common["historyItem"]!["properties"]!["name"]!;

        Assert.Equal(Domain.SessionLimits.MinSlides, common["slideCount"]!["minimum"]!.GetValue<int>());
        Assert.Equal(Domain.SessionLimits.MaxSlides, common["slideCount"]!["maximum"]!.GetValue<int>());
        Assert.Equal(Domain.SessionLimits.MinPlannedDurationMs, start["plannedDurationMs"]!["minimum"]!.GetValue<long>());
        Assert.Equal(Domain.SessionLimits.MaxPlannedDurationMs, start["plannedDurationMs"]!["maximum"]!.GetValue<long>());
        Assert.Equal(Domain.SessionLimits.MaxNameLength, start["name"]!["maxLength"]!.GetValue<int>());
        Assert.Equal(Domain.SessionLimits.MaxNameLength, historyName["maxLength"]!.GetValue<int>());
        Assert.Equal(ProtocolInfo.MaxIdLength, Contracts.ReadSchemaJson("envelope.schema.json")["properties"]!["id"]!["maxLength"]!.GetValue<int>());
    }

    [Fact]
    public void Protocol_v1_md_mentions_every_message_type()
    {
        var document = File.ReadAllText(Contracts.ProtocolDocument);

        foreach (var type in ProtocolSerializer.KnownTypes)
        {
            Assert.Contains($"`{type}`", document);
        }
    }

    [Fact]
    public void Every_schema_declares_draft_2020_12_and_a_unique_id()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(Contracts.SchemasDir, "*.schema.json"))
        {
            var node = JsonNode.Parse(File.ReadAllText(path))!;
            Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)node["$schema"]);
            var id = (string?)node["$id"];
            Assert.False(string.IsNullOrEmpty(id), path);
            Assert.True(ids.Add(id!), $"Duplicate $id in {path}");
        }

        Assert.Equal(21, ids.Count); // 19 message schemas + envelope + common
    }

    [Fact]
    public void Example_and_schema_files_are_valid_utf8_json()
    {
        var files = Directory.GetFiles(Contracts.Root, "*.json", SearchOption.AllDirectories);

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(file));
            Assert.NotEqual(JsonValueKind.Undefined, document.RootElement.ValueKind);
        }
    }

    [Theory]
    [InlineData("targetWindowUnavailable", ErrorCode.TargetWindowUnavailable)]
    [InlineData("keyDeliveryFailed", ErrorCode.KeyDeliveryFailed)]
    public void The_additive_key_delivery_codes_have_their_documented_wire_names(string wire, ErrorCode code)
    {
        Assert.Equal(wire, JsonNamingPolicy.CamelCase.ConvertName(code.ToString()));
        Assert.Contains(wire, SchemaEnum("errorCode"));

        var json = ProtocolSerializer.Serialize(new CommandResultMessage("s-1", new CommandResultPayload("m-1", false, code)));
        Assert.Contains("\"errorCode\":\"" + wire + "\"", json.Replace(" ", string.Empty), StringComparison.Ordinal);
        Assert.True(ProtocolSerializer.Parse(json, () => "s-x").Succeeded);
    }
}
