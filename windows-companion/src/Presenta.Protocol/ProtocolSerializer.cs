using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Presenta.Protocol;

/// <summary>
/// Outcome of parsing an incoming message. Exactly one of <see cref="Message"/> and <see cref="Error"/> is set.
/// <see cref="Error"/> is a ready-to-send <c>error</c> message; parsing never throws for bad input.
/// </summary>
public sealed record ParseResult(ProtocolMessage? Message, ErrorMessage? Error)
{
    public bool Succeeded => Message is not null;
}

/// <summary>
/// Serializes and strictly parses protocol v1 messages (BCL only). Parsing rules:
/// numbers sent as strings are rejected, timestamps must end in "Z", enum values must be known,
/// required payload properties must be present, unknown extra properties are ignored,
/// and unknown message types produce an <c>invalidMessage</c> error instead of throwing.
/// </summary>
public static class ProtocolSerializer
{
    private sealed record Registration(Type PayloadType, Func<string, object, ProtocolMessage> Create);

    private static Registration Register<TPayload>(Func<string, TPayload, ProtocolMessage> create)
        where TPayload : notnull =>
        new(typeof(TPayload), (id, payload) => create(id, (TPayload)payload));

    private static readonly Dictionary<string, Registration> Registry = new(StringComparer.Ordinal)
    {
        [MessageTypes.Hello] = Register<HelloPayload>((i, p) => new HelloMessage(i, p)),
        [MessageTypes.SessionStart] = Register<SessionStartPayload>((i, p) => new SessionStartMessage(i, p)),
        [MessageTypes.SessionPause] = Register<EmptyPayload>((i, p) => new SessionPauseMessage(i, p)),
        [MessageTypes.SessionResume] = Register<EmptyPayload>((i, p) => new SessionResumeMessage(i, p)),
        [MessageTypes.SessionFinish] = Register<EmptyPayload>((i, p) => new SessionFinishMessage(i, p)),
        [MessageTypes.SlideNext] = Register<EmptyPayload>((i, p) => new SlideNextMessage(i, p)),
        [MessageTypes.SlidePrevious] = Register<EmptyPayload>((i, p) => new SlidePreviousMessage(i, p)),
        [MessageTypes.SlideSet] = Register<SlideSetPayload>((i, p) => new SlideSetMessage(i, p)),
        [MessageTypes.AlertAcknowledge] = Register<EmptyPayload>((i, p) => new AlertAcknowledgeMessage(i, p)),
        [MessageTypes.HistoryList] = Register<HistoryListPayload>((i, p) => new HistoryListMessage(i, p)),
        [MessageTypes.Ping] = Register<EmptyPayload>((i, p) => new PingMessage(i, p)),
        [MessageTypes.HelloAccepted] = Register<HelloAcceptedPayload>((i, p) => new HelloAcceptedMessage(i, p)),
        [MessageTypes.HelloRejected] = Register<HelloRejectedPayload>((i, p) => new HelloRejectedMessage(i, p)),
        [MessageTypes.StateUpdate] = Register<StatePayload>((i, p) => new StateUpdateMessage(i, p)),
        [MessageTypes.CommandResult] = Register<CommandResultPayload>((i, p) => new CommandResultMessage(i, p)),
        [MessageTypes.SessionSummary] = Register<HistoryItemPayload>((i, p) => new SessionSummaryMessage(i, p)),
        [MessageTypes.HistoryListResult] = Register<HistoryListResultPayload>((i, p) => new HistoryListResultMessage(i, p)),
        [MessageTypes.Error] = Register<ErrorPayload>((i, p) => new ErrorMessage(i, p)),
        [MessageTypes.Pong] = Register<EmptyPayload>((i, p) => new PongMessage(i, p)),
    };

    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>All message types known to protocol v1.</summary>
    public static IReadOnlyCollection<string> KnownTypes => Registry.Keys;

    private sealed record Envelope(int Version, string Type, string Id, object Payload);

    public static string Serialize(ProtocolMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return JsonSerializer.Serialize(
            new Envelope(ProtocolInfo.Version, message.Type, message.Id, message.PayloadObject),
            Options);
    }

    /// <summary>Parses an incoming message. Never throws for malformed input.</summary>
    /// <param name="json">The JSON text of one message.</param>
    /// <param name="errorIdFactory">Creates the id of an error reply; defaults to a random GUID.</param>
    public static ParseResult Parse(string json, Func<string>? errorIdFactory = null)
    {
        errorIdFactory ??= () => Guid.NewGuid().ToString("N");
        string? knownId = null;

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail(ErrorCode.InvalidMessage, "The message must be a JSON object.");
            }

            if (root.TryGetProperty("id", out var idElement)
                && idElement.ValueKind == JsonValueKind.String
                && idElement.GetString() is { Length: > 0 and <= ProtocolInfo.MaxIdLength } validId)
            {
                knownId = validId;
            }

            if (!root.TryGetProperty("version", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out var version))
            {
                return Fail(ErrorCode.InvalidMessage, "The 'version' property must be an integer.");
            }

            if (version != ProtocolInfo.Version)
            {
                return Fail(ErrorCode.UnsupportedVersion, $"Protocol version {version} is not supported; expected {ProtocolInfo.Version}.");
            }

            if (!root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(typeElement.GetString()))
            {
                return Fail(ErrorCode.InvalidMessage, "The 'type' property must be a non-empty string.");
            }

            if (knownId is null)
            {
                return Fail(ErrorCode.InvalidMessage, $"The 'id' property must be a string of 1 to {ProtocolInfo.MaxIdLength} characters.");
            }

            if (!root.TryGetProperty("payload", out var payloadElement) || payloadElement.ValueKind != JsonValueKind.Object)
            {
                return Fail(ErrorCode.InvalidMessage, "The 'payload' property must be a JSON object.");
            }

            var type = typeElement.GetString()!;
            if (!Registry.TryGetValue(type, out var registration))
            {
                return Fail(ErrorCode.InvalidMessage, $"Unknown message type '{Truncate(type)}'.");
            }

            var payload = JsonSerializer.Deserialize(payloadElement.GetRawText(), registration.PayloadType, Options);
            if (payload is null)
            {
                return Fail(ErrorCode.InvalidMessage, $"The payload of '{type}' is empty.");
            }

            return new ParseResult(registration.Create(knownId, payload), null);
        }
        catch (JsonException ex)
        {
            return Fail(ErrorCode.InvalidMessage, $"Invalid message: {Truncate(ex.Message)}");
        }

        ParseResult Fail(ErrorCode code, string message) =>
            new(null, new ErrorMessage(errorIdFactory(), new ErrorPayload(code, message, knownId)));
    }

    private static string Truncate(string value) => value.Length <= 200 ? value : value[..200];

    private static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new UtcTimestampConverter(),
        },
    };
}
