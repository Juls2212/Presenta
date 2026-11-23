using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Presenta.Domain;
using Presenta.Domain.History;
using Presenta.Domain.Keyboard;
using Presenta.Protocol;
using Wire = Presenta.Protocol;

namespace Presenta.Server;

/// <summary>
/// The companion's brain: pairing, the single watch connection, the one active session, keyboard control, history and
/// the disconnection policy. It knows nothing about Kestrel; <see cref="CompanionHost"/> hands it accepted WebSockets.
/// <para>
/// Concurrency: every access to the session, the connection state and the history goes through one gate, so commands,
/// timer ticks and disconnects are strictly serialized. Time comes only from the injected <see cref="TimeProvider"/>.
/// </para>
/// <para>
/// Watch contract: send <c>hello</c> first; then send a <c>ping</c> about every 2 seconds (the connection is dropped after
/// <see cref="CompanionServerOptions.HeartbeatTimeout"/> without any message); after a reconnect, <c>hello.accepted</c>
/// carries the current state.
/// </para>
/// </summary>
public sealed class CompanionService : IAsyncDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly CompanionServerOptions _options;
    private readonly ISlideKeyboard _keyboard;
    private readonly IHistoryStore _history;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly PairingGate _pairing;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<WatchConnection> _connections = new();
    private readonly ITimer _timer;
    private readonly int _refreshEveryTicks;
    private readonly int _saveEveryTicks;
    private readonly IActiveSessionStore _activeSessions;
    private readonly ICompanionSettingsStore _settings;
    private readonly AiRecommendationEnricher? _ai;

    private PresentationSession? _session;
    private WatchConnection? _client;
    private long? _clientLostAt;
    private int _ticksSinceRefresh;
    private int _ticksSinceSave;
    private bool _rememberCode;
    private long _idCounter;
    private bool _disposed;
    private volatile bool _sendKeys;

    public CompanionService(
        CompanionServerOptions options,
        ISlideKeyboard keyboard,
        IHistoryStore history,
        TimeProvider? time = null,
        ILoggerFactory? loggerFactory = null,
        IActiveSessionStore? activeSessions = null,
        ICompanionSettingsStore? settings = null,
        AiRecommendationEnricher? ai = null)
    {
        _options = options;
        _keyboard = keyboard;
        _history = history;
        _time = time ?? TimeProvider.System;
        _log = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<CompanionService>();
        _activeSessions = activeSessions ?? NullActiveSessionStore.Instance;
        _settings = settings ?? NullCompanionSettingsStore.Instance;
        _ai = ai;
        if (_ai is not null)
        {
            _ai.TextReady += OnAiTextReady;
        }

        // A fixed code from the options (tests) wins; otherwise the stored code, when the user asked to remember it.
        var stored = _settings.Load();
        _rememberCode = stored.RememberPairingCode;
        var initialCode = options.PairingCode
            ?? (_rememberCode && CompanionSettings.IsValidCode(stored.PairingCode) ? stored.PairingCode : null);
        _pairing = new PairingGate(initialCode, options.LockoutThreshold, options.LockoutDuration);
        if (_rememberCode && !string.Equals(stored.PairingCode, _pairing.Code, StringComparison.Ordinal))
        {
            SaveSettings();
        }

        _sendKeys = options.SendKeys;
        _refreshEveryTicks = Math.Max(1, (int)Math.Round(options.RefreshInterval / options.TickInterval));
        _saveEveryTicks = Math.Max(1, (int)Math.Round(options.ActiveSessionSaveInterval / options.TickInterval));
        Status = BuildStatus();
        _timer = _time.CreateTimer(_ => _ = RunTickAsync(), null, options.TickInterval, options.TickInterval);
    }

    /// <summary>Latest status for the window. Replaced on every change.</summary>
    public CompanionStatus Status { get; private set; }

    /// <summary>Raised (inside the service gate, so handlers must return quickly) whenever <see cref="Status"/> changes.</summary>
    public event EventHandler<CompanionStatus>? StatusChanged;

    /// <summary>Raised for each noteworthy event, for the activity list.</summary>
    public event EventHandler<CompanionActivity>? ActivityOccurred;

    /// <summary>The optional AI that words the recommendation (ADR-75), or null when this service has none.</summary>
    public AiRecommendationEnricher? Ai => _ai;

    /// <summary>The current pairing code. Show it to the user; never log it.</summary>
    public string PairingCode => _pairing.Code;

    /// <summary>Whether Next and Previous press keys. When off, commands still move the counter.</summary>
    public bool SendKeys => _sendKeys;

    /// <summary>Whether the pairing code is kept across restarts (ADR-69).</summary>
    public bool RememberPairingCode => _rememberCode;

    /// <summary>
    /// Creates a new pairing code. A watch that is already paired stays connected. When the code is remembered, the new
    /// one replaces the stored one.
    /// </summary>
    public void RegeneratePairingCode()
    {
        _pairing.Regenerate();
        WithGate(() =>
        {
            SaveSettings();
            Publish();
        });
    }

    /// <summary>Turns "remember the pairing code" on or off. Off removes the stored code from the file.</summary>
    public void SetRememberPairingCode(bool remember)
    {
        WithGate(() =>
        {
            _rememberCode = remember;
            SaveSettings();
        });
    }

    private void SaveSettings()
    {
        // Only the outcome is logged, never the code.
        if (!_settings.Save(new CompanionSettings(_rememberCode, _rememberCode ? _pairing.Code : null)))
        {
            _log.LogWarning("The settings file could not be written.");
        }
    }

    public void SetSendKeys(bool enabled)
    {
        _sendKeys = enabled;
        WithGate(Publish);
    }

    /// <summary>Runs for the lifetime of one accepted WebSocket and returns when it is finished.</summary>
    public async Task HandleConnectionAsync(WebSocket socket, IPAddress? remoteAddress, CancellationToken cancellationToken)
    {
        var connection = new WatchConnection(socket, NormalizeRemote(remoteAddress), _time.GetTimestamp());
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                connection.Close(WebSocketCloseStatus.EndpointUnavailable, "server is stopping");
            }
            else
            {
                _connections.Add(connection);
            }
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await ReceiveLoopAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // The peer vanished or the server is stopping: treated as a disconnect below.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected failure in a watch connection.");
        }
        finally
        {
            connection.Complete();
            await OnConnectionEndedAsync(connection).ConfigureAwait(false);
            await connection.SendLoop.ConfigureAwait(false);
            if (socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
                {
                    // Already gone.
                }
            }

            connection.Ended.TrySetResult();
        }
    }

    /// <summary>Asks every connection to close and waits briefly for the close handshakes to finish. Used before the server stops.</summary>
    public async Task CloseAllConnectionsAsync()
    {
        WatchConnection[] snapshot;
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            snapshot = _connections.ToArray();
            foreach (var connection in snapshot)
            {
                connection.Close(WebSocketCloseStatus.EndpointUnavailable, "server is stopping");
            }
        }
        finally
        {
            _gate.Release();
        }

        if (snapshot.Length > 0)
        {
            await Task.WhenAny(Task.WhenAll(snapshot.Select(c => c.Ended.Task)), Task.Delay(TimeSpan.FromSeconds(4))).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ends a running or paused session as interrupted by disconnection and saves exactly one history record, as when
    /// the watch does not come back. Used when the companion is closed by hand during a session (ADR-64). Returns false
    /// (and saves nothing) when there is no running or paused session, so calling it twice never duplicates a record.
    /// </summary>
    public async Task<bool> InterruptActiveSessionAsync()
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed || _session is not { State: Domain.SessionState.Running or Domain.SessionState.Paused } session)
            {
                return false;
            }

            if (!session.MarkInterrupted().Succeeded)
            {
                return false;
            }

            await EndSessionAsync(notifyWatch: true).ConfigureAwait(false);
            BroadcastState();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Looks for a session that was running or paused when the companion last stopped without ending it (a crash or a
    /// power cut) and saves it to the history as interrupted by disconnection, exactly once (ADR-67). The session is not
    /// resumed. A corrupt file is discarded. When saving fails the file is kept and the next start tries again; when the
    /// file cannot be removed after saving, the next start finds the record already in the history and only removes the
    /// file. Returns true when a record was recovered.
    /// </summary>
    public async Task<bool> RecoverInterruptedSessionAsync()
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed || _session is not null)
            {
                return false;
            }

            var load = _activeSessions.Load();
            if (load.Status == ActiveSessionLoadStatus.None)
            {
                return false;
            }

            if (load.Status == ActiveSessionLoadStatus.Invalid || load.Record is null)
            {
                _log.LogWarning("The saved active session was unreadable and was discarded.");
                _activeSessions.Delete();
                return false;
            }

            var summary = ActiveSessionRecovery.BuildInterruptedSummary(load.Record, _options.SessionOptions);
            try
            {
                if (!await IsAlreadyInHistoryAsync(summary).ConfigureAwait(false))
                {
                    await _history.SaveAsync(summary).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Saving the recovered session failed; it will be tried again at the next start.");
                return false;
            }

            if (!_activeSessions.Delete())
            {
                _log.LogWarning("The saved active session could not be removed.");
            }

            Activity(ActivityKind.SessionRecovered);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    // The summary built from one record is always the same, so an equal entry means the record was already saved.
    private async Task<bool> IsAlreadyInHistoryAsync(SessionSummary summary)
    {
        try
        {
            var recent = await _history.ListNewestFirstAsync(100).ConfigureAwait(false);
            return recent.Any(item =>
                item.FinalState == Domain.FinalState.InterruptedByDisconnection
                && item.Name == summary.Name
                && item.FinishedAtUtc.ToUnixTimeMilliseconds() == summary.FinishedAtUtc.ToUnixTimeMilliseconds()
                && item.ActualDurationMs == summary.ActualDurationMs
                && item.PlannedDurationMs == summary.PlannedDurationMs
                && item.PlannedSlides == summary.PlannedSlides
                && item.RegisteredSlides == summary.RegisteredSlides);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reading the history to look for a duplicate failed.");
            return false;
        }
    }

    // Writes the running or paused session to disk. Call with the gate held. Never throws: a failed write only logs.
    private void PersistActiveSession()
    {
        _ticksSinceSave = 0;
        if (_session is not { State: Domain.SessionState.Running or Domain.SessionState.Paused } session)
        {
            return;
        }

        try
        {
            var snapshot = session.GetSnapshot();
            var saved = _activeSessions.Save(new ActiveSessionRecord(
                snapshot.Name,
                snapshot.TotalSlides,
                snapshot.PlannedDurationMs,
                snapshot.CurrentSlide,
                snapshot.ElapsedMs,
                snapshot.State == Domain.SessionState.Paused,
                _time.GetUtcNow()));
            if (!saved)
            {
                _log.LogWarning("The active session could not be saved.");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Saving the active session failed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }
        finally
        {
            _gate.Release();
        }

        if (_ai is not null)
        {
            _ai.TextReady -= OnAiTextReady;
        }

        await _timer.DisposeAsync().ConfigureAwait(false);
        await CloseAllConnectionsAsync().ConfigureAwait(false);
    }

    // ---- receiving ----

    private async Task ReceiveLoopAsync(WatchConnection connection, CancellationToken cancellationToken)
    {
        var socket = connection.Socket;
        var buffer = new byte[4096];
        using var message = new MemoryStream();
        while (!connection.Closing && socket.State == WebSocketState.Open)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    _log.LogWarning("Closing a connection that sent a binary frame.");
                    connection.Close(WebSocketCloseStatus.InvalidMessageType, "only text frames are supported");
                    return;
                }

                if (message.Length + result.Count > _options.MaxFrameBytes)
                {
                    _log.LogWarning("Closing a connection that sent a message larger than {Limit} bytes.", _options.MaxFrameBytes);
                    connection.Close(WebSocketCloseStatus.MessageTooBig, "message is too large");
                    return;
                }

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            string text;
            try
            {
                text = StrictUtf8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }
            catch (DecoderFallbackException)
            {
                connection.Close(WebSocketCloseStatus.InvalidPayloadData, "text frames must be valid UTF-8");
                return;
            }

            connection.Touch(_time.GetTimestamp());
            await ProcessTextAsync(connection, text).ConfigureAwait(false);
        }
    }

    private async Task ProcessTextAsync(WatchConnection connection, string text)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed || connection.Closing)
            {
                return;
            }

            var parsed = ProtocolSerializer.Parse(text, NextId);
            if (!connection.Paired)
            {
                HandleUnpaired(connection, parsed);
                return;
            }

            if (!parsed.Succeeded)
            {
                Send(connection, parsed.Error!);
                return;
            }

            await HandleCommandAsync(connection, parsed.Message!).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected failure while handling a message.");
            Send(connection, new ErrorMessage(NextId(), new ErrorPayload(Wire.ErrorCode.InternalError, "Unexpected failure while handling the message.")));
        }
        finally
        {
            _gate.Release();
        }
    }

    private void HandleUnpaired(WatchConnection connection, ParseResult parsed)
    {
        if (!parsed.Succeeded)
        {
            if (parsed.Error!.Payload.Code == Wire.ErrorCode.UnsupportedVersion)
            {
                Reject(connection, Wire.ErrorCode.UnsupportedVersion, null);
            }
            else
            {
                Send(connection, parsed.Error);
            }

            return;
        }

        if (parsed.Message is not HelloMessage hello)
        {
            Send(connection, new ErrorMessage(
                NextId(),
                new ErrorPayload(Wire.ErrorCode.NotPaired, "Send hello with the pairing code before any other message.", parsed.Message!.Id)));
            return;
        }

        if (hello.Payload.ProtocolVersion != ProtocolInfo.Version)
        {
            Reject(connection, Wire.ErrorCode.UnsupportedVersion, null);
            return;
        }

        switch (_pairing.Check(connection.RemoteKey, hello.Payload.PairingCode, _time.GetUtcNow(), out var retryAfter))
        {
            case PairingOutcome.Accepted:
                AcceptClient(connection);
                break;
            case PairingOutcome.LockedOut:
                Activity(ActivityKind.PairingLockedOut);
                Reject(connection, Wire.ErrorCode.PairingLockedOut, (long)Math.Ceiling(retryAfter.TotalMilliseconds));
                break;
            default:
                Activity(ActivityKind.PairingRejected);
                Reject(connection, Wire.ErrorCode.InvalidPairingCode, null);
                break;
        }
    }

    private void Reject(WatchConnection connection, Wire.ErrorCode reason, long? retryAfterMs)
    {
        Send(connection, new HelloRejectedMessage(NextId(), new HelloRejectedPayload(reason, retryAfterMs)));
        connection.Close(WebSocketCloseStatus.PolicyViolation, "pairing rejected");
    }

    private void AcceptClient(WatchConnection connection)
    {
        if (_client is { } previous && !ReferenceEquals(previous, connection))
        {
            previous.Close(WebSocketCloseStatus.NormalClosure, "replaced by a newer connection");
            Activity(ActivityKind.WatchReplaced);
        }

        _client = connection;
        connection.Paired = true;
        _clientLostAt = null;
        Activity(ActivityKind.WatchConnected);
        Publish(); // before the reply, so a test or UI that reacts to the reply already sees the new status
        Send(connection, new HelloAcceptedMessage(NextId(), new HelloAcceptedPayload(ProtocolInfo.Version, BuildState())));
    }

    private async Task OnConnectionEndedAsync(WatchConnection connection)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _connections.Remove(connection);
            if (ReferenceEquals(_client, connection))
            {
                _client = null;
                if (_session is not null)
                {
                    _clientLostAt = _time.GetTimestamp();
                    _log.LogInformation("The watch disconnected during a session; the session keeps running.");
                }

                Activity(ActivityKind.WatchDisconnected);
                Publish();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---- commands ----

    private async Task HandleCommandAsync(WatchConnection connection, ProtocolMessage message)
    {
        switch (message)
        {
            case PingMessage:
                Send(connection, new PongMessage(NextId(), new EmptyPayload()));
                return;
            case HelloMessage hello:
                Send(connection, new ErrorMessage(
                    NextId(),
                    new ErrorPayload(Wire.ErrorCode.InvalidState, "The connection is already paired.", hello.Id)));
                return;
            case HistoryListMessage list:
                await HandleHistoryListAsync(connection, list).ConfigureAwait(false);
                return;
            case SessionFinishMessage finish:
                await HandleFinishAsync(connection, finish.Id).ConfigureAwait(false);
                return;
        }

        var error = message switch
        {
            SessionStartMessage m => StartSession(m.Payload),
            SessionPauseMessage => WithSession(s => ToError(s.Pause())),
            SessionResumeMessage => WithSession(s => ToError(s.Resume())),
            SlideNextMessage => WithSession(s => MoveSlide(s, SlideDirection.Next)),
            SlidePreviousMessage => WithSession(s => MoveSlide(s, SlideDirection.Previous)),
            SlideSetMessage m => WithSession(s => SetSlide(s, m.Payload.Slide)),
            AlertAcknowledgeMessage => WithSession(s =>
            {
                s.AcknowledgeAlert();
                return null;
            }),
            _ => Wire.ErrorCode.InvalidMessage,
        };

        switch (message)
        {
            case SessionPauseMessage when error is null:
                Activity(ActivityKind.SessionPaused);
                break;
            case SessionResumeMessage when error is null:
                Activity(ActivityKind.SessionResumed);
                break;
        }

        // Written before the answer, so what the watch was told is also what survives a crash.
        PersistActiveSession();
        SendResult(connection, message.Id, error);
        BroadcastState();
    }

    private async Task HandleFinishAsync(WatchConnection connection, string correlationId)
    {
        if (_session is null)
        {
            SendResult(connection, correlationId, Wire.ErrorCode.NoActiveSession);
            return;
        }

        var result = _session.Finish();
        if (!result.Succeeded)
        {
            SendResult(connection, correlationId, ToError(result));
            return;
        }

        SendResult(connection, correlationId, null);
        await EndSessionAsync(notifyWatch: true).ConfigureAwait(false);
        BroadcastState();
    }

    private async Task HandleHistoryListAsync(WatchConnection connection, HistoryListMessage message)
    {
        var limit = Math.Clamp(message.Payload.Limit ?? _options.DefaultHistoryLimit, 1, 100);
        try
        {
            var items = await _history.ListNewestFirstAsync(limit).ConfigureAwait(false);
            Send(connection, new HistoryListResultMessage(
                NextId(),
                new HistoryListResultPayload(items.Select(ProtocolMapper.ToPayload).ToList())));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Reading the history failed.");
            Send(connection, new ErrorMessage(
                NextId(),
                new ErrorPayload(Wire.ErrorCode.InternalError, "The history could not be read.", message.Id)));
        }
    }

    private Wire.ErrorCode? StartSession(SessionStartPayload payload)
    {
        if (_session is not null)
        {
            return Wire.ErrorCode.InvalidState;
        }

        if (_sendKeys && !TargetIsUsable())
        {
            Activity(ActivityKind.StartRejectedNoTarget);
            return Wire.ErrorCode.TargetWindowUnavailable;
        }

        var name = string.IsNullOrWhiteSpace(payload.Name)
            ? "Session " + _time.GetLocalNow().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : payload.Name;
        var configuration = SessionConfiguration.Create(payload.PlannedSlides, TimeSpan.FromMilliseconds(payload.PlannedDurationMs), name);
        if (!configuration.Succeeded)
        {
            return ProtocolMapper.ToErrorCode(configuration.Error!.Value);
        }

        var session = new PresentationSession(configuration.Value!, _time, _options.SessionOptions);
        var started = session.Start();
        if (!started.Succeeded)
        {
            return ProtocolMapper.ToErrorCode(started.Error!.Value);
        }

        _session = session;
        _ai?.ResetSession();
        Activity(ActivityKind.SessionStarted, session.CurrentSlide);
        return null;
    }

    private Wire.ErrorCode? WithSession(Func<PresentationSession, Wire.ErrorCode?> action) =>
        _session is null ? Wire.ErrorCode.NoActiveSession : action(_session);

    private static Wire.ErrorCode? ToError(OperationResult result) =>
        result.Succeeded ? null : ProtocolMapper.ToErrorCode(result.Error!.Value);

    /// <summary>True when keys can be aimed at a presentation window (a minimized one is restored before each key).</summary>
    private bool TargetIsUsable()
    {
        try
        {
            return _keyboard.GetTargetState() is TargetState.Ready or TargetState.Minimized;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Checking the presentation window failed.");
            return false;
        }
    }

    /// <summary>
    /// Next and Previous. The key goes first and the counter second: when the counter would really move and the "send keys"
    /// switch is on, the key is delivered to the confirmed presentation window, and only if that succeeds is the counter
    /// moved. A failed delivery therefore leaves the counter at its previous value (nothing to roll back), reports the
    /// failure and lets the session continue. When the counter would not move (last slide for Next, slide 1 for Previous)
    /// no key is attempted. With the switch off the counter simply moves.
    /// </summary>
    private Wire.ErrorCode? MoveSlide(PresentationSession session, SlideDirection direction)
    {
        var willMove = session.WouldMove(direction);
        var delivered = false;
        if (willMove && _sendKeys)
        {
            var delivery = Deliver(direction);
#if DEBUG
            _log.LogInformation("PresentaDebug key delivery direction={Direction} result={Result}", direction, delivery);
#endif
            if (delivery != KeyDeliveryResult.Delivered)
            {
                Activity(ActivityKind.KeyFailed, session.CurrentSlide, delivery);
                return ToError(delivery);
            }

            delivered = true;
        }

        var result = direction == SlideDirection.Next ? session.Next() : session.Previous();
        if (!result.Succeeded)
        {
            return ProtocolMapper.ToErrorCode(result.Error!.Value);
        }

        if (!result.CounterChanged)
        {
            return null;
        }

        if (!delivered)
        {
            Activity(ActivityKind.SlideCounterChanged, result.CurrentSlide);
            return null;
        }

        Activity(direction == SlideDirection.Next ? ActivityKind.NextKeySent : ActivityKind.PreviousKeySent, result.CurrentSlide);
        return null;
    }

    /// <summary>True in Debug builds only: each key delivery then writes one timing line to the log (RNF 03 measurement).</summary>
#if DEBUG
    public const bool TimingDiagnosticsEnabled = true;
#else
    public const bool TimingDiagnosticsEnabled = false;
#endif

    private KeyDeliveryResult Deliver(SlideDirection direction)
    {
#if DEBUG
        var started = _time.GetTimestamp();
#endif
        try
        {
            return _keyboard.Send(direction);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Sending the {Direction} key failed.", direction);
            return KeyDeliveryResult.SendFailed;
        }
#if DEBUG
        finally
        {
            // Time spent bringing the target window forward and injecting the key. The slideshow repaint is not included.
            _log.LogInformation(
                "PresentaTiming key={Direction} activateAndInjectMs={Milliseconds}",
                direction,
                (long)_time.GetElapsedTime(started).TotalMilliseconds);
        }
#endif
    }

    private static Wire.ErrorCode ToError(KeyDeliveryResult delivery) => delivery switch
    {
        KeyDeliveryResult.NoTargetSelected or KeyDeliveryResult.TargetNotFound or KeyDeliveryResult.TargetAmbiguous
            or KeyDeliveryResult.TargetElevated => Wire.ErrorCode.TargetWindowUnavailable,
        _ => Wire.ErrorCode.KeyDeliveryFailed,
    };

    /// <summary>Manual correction of the counter. It never sends a key.</summary>
    private Wire.ErrorCode? SetSlide(PresentationSession session, int slide)
    {
        var result = session.SetSlide(slide);
        if (!result.Succeeded)
        {
            return ProtocolMapper.ToErrorCode(result.Error!.Value);
        }

        if (result.CounterChanged)
        {
            Activity(ActivityKind.SlideSet, result.CurrentSlide);
        }

        return null;
    }

    /// <summary>Ends the finished or interrupted session exactly once: save a history record, then tell the watch.</summary>
    private async Task EndSessionAsync(bool notifyWatch)
    {
        var session = _session!;
        _session = null;
        _clientLostAt = null;
        _ai?.ResetSession();
        var summary = session.BuildSummary().Value!;
        var saved = false;
        try
        {
            await _history.SaveAsync(summary).ConfigureAwait(false);
            saved = true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Saving the history record failed.");
        }

        // The file is removed only once the record is safe in the history. If saving failed it stays, so that the next
        // start can still recover the session as interrupted.
        if (saved && !_activeSessions.Delete())
        {
            _log.LogWarning("The saved active session could not be removed.");
        }

        Activity(summary.FinalState == Domain.FinalState.InterruptedByDisconnection ? ActivityKind.SessionInterrupted : ActivityKind.SessionFinished);
        if (notifyWatch && _client is { Closing: false } client)
        {
            Send(client, new SessionSummaryMessage(NextId(), ProtocolMapper.ToPayload(summary)));
        }
    }

    // ---- time ----

    private async Task RunTickAsync()
    {
        try
        {
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_disposed)
                {
                    return;
                }

                foreach (var connection in _connections)
                {
                    if (!connection.Closing && _time.GetElapsedTime(connection.LastReceivedTimestamp) > _options.HeartbeatTimeout)
                    {
                        _log.LogInformation("Closing a connection that has been silent for too long.");
                        connection.Close(WebSocketCloseStatus.PolicyViolation, "heartbeat timeout");
                    }
                }

                var changed = false;
                if (_session is { State: Domain.SessionState.Running } running && running.Tick() is not null)
                {
                    Activity(ActivityKind.AlertRaised);
                    changed = true;
                }

                if (_session is not null && _client is null && _clientLostAt is { } lostAt
                    && _time.GetElapsedTime(lostAt) >= _options.DisconnectGracePeriod)
                {
                    _log.LogInformation("No watch came back within the grace period; ending the session as interrupted.");
                    _session.MarkInterrupted();
                    await EndSessionAsync(notifyWatch: false).ConfigureAwait(false);
                    changed = true;
                }

                _ticksSinceSave++;
                if (_session is not null && (changed || _ticksSinceSave >= _saveEveryTicks))
                {
                    PersistActiveSession();
                }

                _ticksSinceRefresh++;
                if (changed || (_client is not null && _ticksSinceRefresh >= _refreshEveryTicks))
                {
                    _ticksSinceRefresh = 0;
                    BroadcastState();
                }
                else if (_session is not null)
                {
                    Publish();
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "The periodic tick failed.");
        }
    }

    // ---- helpers (call with the gate held) ----

    private StatePayload BuildState() =>
        new(_time.GetUtcNow(), _session is null ? null : ProtocolMapper.ToPayload(WithAi(_session.GetSnapshot())));

    // Fills the optional text of the recommendation from the cache. Never waits for the provider.
    private SessionSnapshot WithAi(SessionSnapshot snapshot) =>
        _ai is null ? snapshot : snapshot with { Recommendation = _ai.Apply(snapshot) };

    // A text arrived in the background: send the watch a new state without waiting for the next periodic one.
    private void OnAiTextReady(object? sender, EventArgs e) => _ = PushStateAsync();

    private async Task PushStateAsync()
    {
        try
        {
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (!_disposed && _session is not null)
                {
                    BroadcastState();
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Sending the state after an AI text failed.");
        }
    }

    private void BroadcastState()
    {
        Publish(); // before the message, so anything reacting to the message already sees the new status
        if (_client is { Closing: false } client)
        {
            Send(client, new StateUpdateMessage(NextId(), BuildState()));
        }
    }

    private void SendResult(WatchConnection connection, string correlationId, Wire.ErrorCode? error) =>
        Send(connection, new CommandResultMessage(NextId(), new CommandResultPayload(correlationId, error is null, error)));

    private static void Send(WatchConnection connection, ProtocolMessage message) =>
        connection.Send(ProtocolSerializer.Serialize(message));

    private string NextId() => "s-" + Interlocked.Increment(ref _idCounter).ToString(CultureInfo.InvariantCulture);

    private CompanionStatus BuildStatus() => new(_client is not null, _pairing.Code, _sendKeys, _session?.GetSnapshot());

    private void Publish()
    {
        Status = BuildStatus();
        try
        {
            StatusChanged?.Invoke(this, Status);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A status handler failed.");
        }
    }

    private void Activity(ActivityKind kind, int? slide = null, KeyDeliveryResult? keyResult = null)
    {
        try
        {
            ActivityOccurred?.Invoke(this, new CompanionActivity(_time.GetUtcNow(), kind, slide, keyResult));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "An activity handler failed.");
        }
    }

    private void WithGate(Action action)
    {
        _gate.Wait();
        try
        {
            action();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string NormalizeRemote(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }
}
