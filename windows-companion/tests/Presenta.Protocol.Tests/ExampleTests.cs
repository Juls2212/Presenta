using System.Text.Json.Nodes;
using Presenta.Protocol.Tests.Support;

namespace Presenta.Protocol.Tests;

public class ExampleTests
{
    public static IEnumerable<object[]> ValidExamples() =>
        Directory.GetFiles(Contracts.ExamplesDir, "*.json").Order().Select(f => new object[] { Path.GetFileName(f) });

    public static IEnumerable<object[]> InvalidExamples() =>
        Directory.GetFiles(Contracts.InvalidExamplesDir, "*.json").Order().Select(f => new object[] { Path.GetFileName(f) });

    /// <summary>The message type is the part of the file name before the first "--".</summary>
    private static string TypeOf(string fileName)
    {
        var withoutExtension = fileName[..^".json".Length];
        var separator = withoutExtension.IndexOf("--", StringComparison.Ordinal);
        return separator < 0 ? withoutExtension : withoutExtension[..separator];
    }

    [Fact]
    public void Every_message_type_has_a_schema_and_at_least_one_valid_example()
    {
        var exampleTypes = Directory.GetFiles(Contracts.ExamplesDir, "*.json")
            .Select(f => TypeOf(Path.GetFileName(f)))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var type in ProtocolSerializer.KnownTypes)
        {
            Assert.Contains(type, Contracts.MessageSchemaTypes);
            Assert.Contains(type, exampleTypes);
        }

        Assert.Equal(
            ProtocolSerializer.KnownTypes.Order(StringComparer.Ordinal),
            Contracts.MessageSchemaTypes.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ValidExamples))]
    public void Valid_example_validates_against_its_schema(string fileName)
    {
        var json = File.ReadAllText(Path.Combine(Contracts.ExamplesDir, fileName));
        var declaredType = (string?)JsonNode.Parse(json)!["type"];

        Assert.Equal(TypeOf(fileName), declaredType);
        Assert.True(Contracts.IsValid(TypeOf(fileName), json), fileName);
    }

    [Theory]
    [MemberData(nameof(ValidExamples))]
    public void Valid_example_is_accepted_by_the_parser_and_re_serializes_stably(string fileName)
    {
        var json = File.ReadAllText(Path.Combine(Contracts.ExamplesDir, fileName));

        var parsed = ProtocolSerializer.Parse(json);

        Assert.True(parsed.Succeeded, parsed.Error?.Payload.Message);
        var again = ProtocolSerializer.Serialize(parsed.Message!);
        Assert.True(Contracts.IsValid(TypeOf(fileName), again), fileName);
        var reparsed = ProtocolSerializer.Parse(again);
        Assert.True(reparsed.Succeeded);
        Assert.Equal(again, ProtocolSerializer.Serialize(reparsed.Message!));
    }

    [Theory]
    [MemberData(nameof(InvalidExamples))]
    public void Invalid_example_is_rejected_by_the_schema(string fileName)
    {
        var json = File.ReadAllText(Path.Combine(Contracts.InvalidExamplesDir, fileName));

        Assert.False(Contracts.IsValid(TypeOf(fileName), json), fileName);
    }

    [Theory]
    [MemberData(nameof(InvalidExamples))]
    public void Invalid_example_is_rejected_by_the_parser_unless_it_is_schema_only(string fileName)
    {
        var json = File.ReadAllText(Path.Combine(Contracts.InvalidExamplesDir, fileName));

        var parsed = ProtocolSerializer.Parse(json);

        if (fileName.Contains("schema-only", StringComparison.Ordinal))
        {
            Assert.True(parsed.Succeeded, $"{fileName} is marked schema-only but the parser rejected it.");
        }
        else
        {
            Assert.False(parsed.Succeeded, $"{fileName} should be rejected by the parser.");
            Assert.NotNull(parsed.Error);
            Assert.Contains(parsed.Error.Payload.Code, new[] { ErrorCode.InvalidMessage, ErrorCode.UnsupportedVersion });
        }
    }

    [Fact]
    public void Invalid_examples_include_both_parser_and_schema_only_cases()
    {
        var names = Directory.GetFiles(Contracts.InvalidExamplesDir, "*.json").Select(Path.GetFileName).ToList();

        Assert.Contains(names, n => n!.Contains("schema-only", StringComparison.Ordinal));
        Assert.Contains(names, n => !n!.Contains("schema-only", StringComparison.Ordinal));
    }
}
