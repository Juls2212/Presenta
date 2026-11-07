using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Presenta.Server;

/// <summary>
/// One WebSocket client. All writes go through a queue drained by a single sender loop, so the socket is never written
/// by two callers at once and message order is the order of <see cref="Send"/> calls.
/// </summary>
internal sealed class WatchConnection
{
    private sealed record Outbound(string? Text, WebSocketCloseStatus? Status, string? Reason);

    private static readonly TimeSpan AbortAfterClose = TimeSpan.FromSeconds(2);

    private readonly WebSocket _socket;
    private readonly Channel<Outbound> _outbound = Channel.CreateUnbounded<Outbound>(new UnboundedChannelOptions { SingleReader = true });
    private long _lastReceivedTimestamp;

    public WatchConnection(WebSocket socket, string remoteKey, long nowTimestamp)
    {
        _socket = socket;
        RemoteKey = remoteKey;
        _lastReceivedTimestamp = nowTimestamp;
        SendLoop = RunSendLoopAsync();
    }

    public WebSocket Socket => _socket;

    /// <summary>Remote address used for pairing lockouts.</summary>
    public string RemoteKey { get; }

    public bool Paired { get; set; }

    /// <summary>True once a close has been queued; the receive loop stops reading.</summary>
    public volatile bool Closing;

    public Task SendLoop { get; }

    /// <summary>Completed when the connection has been fully handled (closed and cleaned up).</summary>
    public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public long LastReceivedTimestamp => Interlocked.Read(ref _lastReceivedTimestamp);

    public void Touch(long timestamp) => Interlocked.Exchange(ref _lastReceivedTimestamp, timestamp);

    public void Send(string json) => _outbound.Writer.TryWrite(new Outbound(json, null, null));

    /// <summary>Queues a close frame after everything already queued, then stops accepting messages.</summary>
    public void Close(WebSocketCloseStatus status, string reason)
    {
        Closing = true;
        _outbound.Writer.TryWrite(new Outbound(null, status, reason));
        _outbound.Writer.TryComplete();
    }

    /// <summary>Ends the queue without sending a close frame (the peer already closed).</summary>
    public void Complete() => _outbound.Writer.TryComplete();

    private async Task RunSendLoopAsync()
    {
        try
        {
            await foreach (var item in _outbound.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (item.Text is not null)
                {
                    if (_socket.State != WebSocketState.Open)
                    {
                        continue;
                    }

                    await _socket.SendAsync(Encoding.UTF8.GetBytes(item.Text), WebSocketMessageType.Text, true, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                else
                {
                    if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        await _socket.CloseOutputAsync(item.Status ?? WebSocketCloseStatus.NormalClosure, item.Reason, CancellationToken.None)
                            .ConfigureAwait(false);
                        _ = AbortIfPeerDoesNotAnswerAsync();
                    }

                    break;
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            _socket.Abort();
        }
    }

    /// <summary>
    /// A peer that vanished never answers a close frame. This is a transport-level timeout (real time, not session time).
    /// </summary>
    private async Task AbortIfPeerDoesNotAnswerAsync()
    {
        try
        {
            await Task.Delay(AbortAfterClose).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_socket.State != WebSocketState.Closed && _socket.State != WebSocketState.Aborted)
        {
            _socket.Abort();
        }
    }
}
