using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Presenta.Protocol.Tests.Support;

/// <summary>Locates and loads the language-neutral contracts in the shared-contracts folder.</summary>
internal static class Contracts
{
    private static readonly Lazy<string> RootPath = new(FindRoot);

    private static readonly Lazy<Dictionary<string, JsonSchema>> MessageSchemas = new(LoadMessageSchemas);

    public static string Root => RootPath.Value;

    public static string SchemasDir => Path.Combine(Root, "schemas");

    public static string ExamplesDir => Path.Combine(Root, "examples");

    public static string InvalidExamplesDir => Path.Combine(ExamplesDir, "invalid");

    public static string ProtocolDocument => Path.Combine(Root, "protocol-v1.md");

    /// <summary>Schema file names (without directory) of all per-message schemas.</summary>
    public static IEnumerable<string> MessageSchemaTypes => MessageSchemas.Value.Keys;

    public static JsonNode ReadSchemaJson(string fileName) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(SchemasDir, fileName)))!;

    /// <summary>Validates a full message (envelope plus payload) against the schema of the given message type.</summary>
    public static bool IsValid(string messageType, string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = MessageSchemas.Value[messageType].Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        return result.IsValid;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "shared-contracts");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The shared-contracts folder was not found above the test output directory.");
    }

    private static Dictionary<string, JsonSchema> LoadMessageSchemas()
    {
        var registry = new SchemaRegistry();
        var build = new BuildOptions { SchemaRegistry = registry };
        var result = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);

        foreach (var path in Directory.GetFiles(SchemasDir, "*.schema.json"))
        {
            var schema = JsonSchema.FromText(File.ReadAllText(path), build);
            var name = Path.GetFileName(path)[..^".schema.json".Length];
            if (name is not ("common" or "envelope"))
            {
                result[name] = schema;
            }
        }

        return result;
    }
}
