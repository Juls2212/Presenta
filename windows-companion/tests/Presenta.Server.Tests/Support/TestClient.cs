using System.Net.WebSockets;
using System.Text;
using Presenta.Protocol;

namespace Presenta.Server.Tests.Support;

/// <summary>Raised when the server closed the connection while a test was waiting for a message.</summary>
internal sealed class ServerClosedException : Exception
{
    public ServerClosedException(WebSocketCloseStatus? status)
        : base($"The server closed the connection ({status}).")
    {
        Status = status;
    }

    public WebSocketCloseStatus? Status { get; }
}

/// <summary>A real <see cref="ClientWebSocket"/> that speaks protocol v1 to a server on loopback.</summary>
internal sealed class TestClient : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private TestClient(ClientWebSocket socket)
    {
        Socket = socket;
    }

    public ClientWebSocket Socket { get; }

    public static async Task<TestClient> ConnectAsync(int port)
    {
        var socket = new ClientWebSocket();
        using var cts = new CancellationTokenSource(Timeout);
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);
        return new TestClient(socket);
    }

    public async Task SendAsync(ProtocolMessage message) => await SendRawAsync(ProtocolSerializer.Serialize(message));

    public async Task SendRawAsync(string text)
    {
        using var cts = new CancellationTokenSource(Timeout);
        await Socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cts.Token);
    }

    public async Task SendBinaryAsync(byte[] bytes)
    {
        using var cts = new CancellationTokenSource(Timeout);
        await Socket.SendAsync(bytes, WebSocketMessageType.Binary, true, cts.Token);
    }

    public async Task<ProtocolMessage> ReceiveAsync()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var text = await ReceiveTextAsync(cts.Token);
        var parsed = ProtocolSerializer.Parse(text);
        Assert.True(parsed.Succeeded, "The server sent a message the protocol layer rejects: " + text);
        return parsed.Message!;
    }

    public async Task<T> ReceiveAsync<T>()
        where T : ProtocolMessage
    {
        var message = await ReceiveAsync();
        return Assert.IsType<T>(message);
    }

    /// <summary>Reads messages, skipping others, until one of type <typeparamref name="T"/> matches.</summary>
    public async Task<T> ReceiveUntilAsync<T>(Func<T, bool>? predicate = null)
        where T : ProtocolMessage
    {
        while (true)
        {
            var message = await ReceiveAsync();
            if (message is T typed && (predicate is null || predicate(typed)))
            {
                return typed;
            }
        }
    }

    /// <summary>Sends a command and returns its <c>command.result</c> (other messages are skipped).</summary>
    public async Task<CommandResultMessage> CommandAsync(ProtocolMessage command)
    {
        await SendAsync(command);
        return await ReceiveUntilAsync<CommandResultMessage>(r => r.Payload.CorrelationId == command.Id);
    }

    /// <summary>Waits for the server to close the connection and returns the close status.</summary>
    public async Task<WebSocketCloseStatus?> ExpectCloseAsync()
    {
        try
        {
            while (true)
            {
                await ReceiveAsync();
            }
        }
        catch (ServerClosedException closed)
        {
            await AnswerCloseAsync(); // a polite client completes the close handshake
            return closed.Status;
        }
    }

    private async Task AnswerCloseAsync()
    {
        try
        {
            if (Socket.State == WebSocketState.CloseReceived)
            {
                using var cts = new CancellationTokenSource(Timeout);
                await Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token);
            }
        }
        catch (WebSocketException)
        {
            // The server already dropped the socket.
        }
    }

    public async Task<HelloAcceptedMessage> PairAsync(string code)
    {
        await SendAsync(Msg.Hello(code));
        return await ReceiveAsync<HelloAcceptedMessage>();
    }

    public async Task CloseAsync()
    {
        if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            using var cts = new CancellationTokenSource(Timeout);
            await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test done", cts.Token);
        }
    }

    public ValueTask DisposeAsync()
    {
        Socket.Abort();
        Socket.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<string> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await Socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new ServerClosedException(result.CloseStatus);
            }

            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
    }
}

/// <summary>Builders for client messages with unique correlation ids.</summary>
internal static class Msg
{
    private static int _counter;

    public static string NextId() => "c-" + Interlocked.Increment(ref _counter);

    public static HelloMessage Hello(string code, int protocolVersion = 1) =>
        new(NextId(), new HelloPayload(code, protocolVersion, "test watch"));

    public static SessionStartMessage Start(int slides = 20, long durationMs = 900_000, string? name = "Test session") =>
        new(NextId(), new SessionStartPayload(slides, durationMs, name));

    public static SessionPauseMessage Pause() => new(NextId(), new EmptyPayload());

    public static SessionResumeMessage Resume() => new(NextId(), new EmptyPayload());

    public static SessionFinishMessage Finish() => new(NextId(), new EmptyPayload());

    public static SlideNextMessage Next() => new(NextId(), new EmptyPayload());

    public static SlidePreviousMessage Previous() => new(NextId(), new EmptyPayload());

    public static SlideSetMessage SetSlide(int slide) => new(NextId(), new SlideSetPayload(slide));

    public static AlertAcknowledgeMessage Acknowledge() => new(NextId(), new EmptyPayload());

    public static HistoryListMessage History(int? limit = null) => new(NextId(), new HistoryListPayload(limit));

    public static PingMessage Ping() => new(NextId(), new EmptyPayload());
}
