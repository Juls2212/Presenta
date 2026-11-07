using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Presenta.Protocol;

/// <summary>
/// Strict UTC timestamps: ISO 8601, must end in "Z". Offsets such as "+00:00" and values without a zone are rejected.
/// Written with millisecond precision.
/// </summary>
public sealed partial class UtcTimestampConverter : JsonConverter<DateTimeOffset>
{
    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?Z$")]
    private static partial Regex TimestampPattern();

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("A UTC timestamp must be a JSON string.");
        }

        var text = reader.GetString();
        if (text is null
            || !TimestampPattern().IsMatch(text)
            || !DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var value))
        {
            throw new JsonException("A UTC timestamp must be ISO 8601 and end in 'Z'.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
    }
}
