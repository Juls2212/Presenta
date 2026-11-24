using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Presenta.Ai;
using Presenta.Domain;

namespace Presenta.Server;

/// <summary>What the "Recomendaciones con IA" section of the window shows.</summary>
public enum AiStatus
{
    /// <summary>One of the three environment variables is missing.</summary>
    NotConfigured,

    /// <summary>The address is not a valid http or https address.</summary>
    InvalidAddress,

    /// <summary>The address is plain http to another computer.</summary>
    InsecureAddress,

    /// <summary>Everything is configured but the user has not turned the AI on.</summary>
    Disabled,

    /// <summary>On, and a text will be requested when a situation needs one.</summary>
    Ready,

    /// <summary>A request is in flight.</summary>
    Working,

    /// <summary>The circuit breaker is open after errors: the deterministic wording is used until it closes.</summary>
    Paused,

    /// <summary>The provider refused the key (401 or 403).</summary>
    InvalidKey,

    /// <summary>The last answer was well formed but had no visible text (typically a reasoning model that used up the token limit).</summary>
    NoText,
}

/// <summary>Limits of the enricher. Every default is the documented value (ADR-76).</summary>
public sealed record AiEnrichmentOptions
{
    /// <summary>
    /// At most one request in this time. The first request of each alert sequence is exempt (ADR-81): that is the one the
    /// alert is waiting for.
    /// </summary>
    public TimeSpan MinInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>At most this many requests per presentation.</summary>
    public int MaxCallsPerSession { get; init; } = 30;

    /// <summary>Consecutive failures (timeouts, 5xx, malformed answers, unusable text) that open the circuit breaker.</summary>
    public int FailureThreshold { get; init; } = 3;

    /// <summary>How long the circuit breaker stays open (also after a refused key).</summary>
    public TimeSpan PauseDuration { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>First wait after a 429; it doubles with each consecutive one.</summary>
    public TimeSpan RateLimitBackoffInitial { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan RateLimitBackoffMax { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The enricher gives up on a request after this long, whatever the client does. A safety net above the client's own
    /// timeout (8 s by default, 30 s at most), which is the one that normally ends a slow request.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(35);

    /// <summary>Texts kept by situation.</summary>
    public int CacheSize { get; init; } = 16;

    /// <summary>How long a text may be reused for the same situation (ADR-81). Measured from the moment it was stored.</summary>
    public TimeSpan CacheTimeToLive { get; init; } = TimeSpan.FromSeconds(120);
}

/// <summary>
/// Adds an optional short text to the deterministic recommendation (ADR-75). It never decides anything: the pace, the
/// alerts and the recommendation code come from the domain, and the text is only an extra that is shown when it exists
/// for the current situation. Everything about the provider happens in the background; <see cref="Apply"/> only looks in
/// a cache and, at most, starts a task, so commands, ticks and <c>state.update</c> are never delayed. The key never passes
/// through this class: it lives in the client.
/// <para>
/// A text is requested only while an alert is active (it is only shown then), at most once per situation key, at most
/// once per interval, at most a fixed number of times per presentation, and never while the circuit breaker is open.
/// </para>
/// </summary>
public sealed class AiRecommendationEnricher
{
    private readonly IAiTextClient? _client;
    private readonly AiAvailability _availability;
    private readonly AiEnrichmentOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly HashSet<int> _requestedSequences = new();
    private readonly Queue<string> _cacheOrder = new();

    private bool _enabled;
    private bool _inFlight;
    private Task _pending = Task.CompletedTask;
    private string? _lastRequestedKey;
    private DateTimeOffset? _lastCallAt;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;
    private int _consecutiveFailures;
    private int _rateLimitHits;
    private bool _keyRefused;
    private bool _lastWasNoText;
    private int _calls;
    private string? _lastText;
    private TimeSpan? _lastLatency;
    private int? _activeSequence;
    private DateTimeOffset _activeSeenAt;
    private LiveText? _live;

    /// <summary>True in Debug builds only: each request then writes one timing line (no payload, no response text).</summary>
#if DEBUG
    public const bool TimingDiagnosticsEnabled = true;
#else
    public const bool TimingDiagnosticsEnabled = false;
#endif

    private sealed record CacheEntry(string Text, DateTimeOffset StoredAt);

    // A text that arrived while the alert it was requested for was still active. It belongs to that sequence only.
    private sealed record LiveText(int Sequence, string Code, string Kind, string Text);

    private sealed record RequestContext(int Sequence, string Code, string Kind, DateTimeOffset StartedAt, TimeSpan AlertToRequest);

    public AiRecommendationEnricher(
        IAiTextClient? client,
        AiAvailability availability,
        AiEnrichmentOptions? options = null,
        TimeProvider? time = null,
        ILoggerFactory? loggerFactory = null)
    {
        _client = availability == AiAvailability.Available ? client : null;
        _availability = availability;
        _options = options ?? new AiEnrichmentOptions();
        _time = time ?? TimeProvider.System;
        _log = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<AiRecommendationEnricher>();
    }

    /// <summary>Raised when the status or the last text changed. May run on any thread; handlers must return quickly.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a new text became available for a situation, so the watch can be sent a new state.</summary>
    public event EventHandler? TextReady;

    /// <summary>True when the three settings are valid, so the switch can be turned on.</summary>
    public bool IsAvailable => _client is not null;

    /// <summary>The user's switch. Off by default; it cannot be turned on when the AI is not available.</summary>
    public bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }

        set
        {
            lock (_gate)
            {
                var next = value && _client is not null;
                if (next == _enabled)
                {
                    return;
                }

                _enabled = next;
                // Turning it off and on again is how the user retries after a refused key or a long pause.
                _pausedUntil = DateTimeOffset.MinValue;
                _consecutiveFailures = 0;
                _rateLimitHits = 0;
                _keyRefused = false;
                _lastWasNoText = false;
                _lastRequestedKey = null;
            }

            RaiseChanged();
        }
    }

    /// <summary>The newest text produced and accepted, for the window. Null until there is one.</summary>
    public string? LastText
    {
        get
        {
            lock (_gate)
            {
                return _lastText;
            }
        }
    }

    /// <summary>How long the request that produced <see cref="LastText"/> took. Null until there is a text.</summary>
    public TimeSpan? LastLatency
    {
        get
        {
            lock (_gate)
            {
                return _lastLatency;
            }
        }
    }

    /// <summary>Requests started in the current presentation.</summary>
    public int CallsThisSession
    {
        get
        {
            lock (_gate)
            {
                return _calls;
            }
        }
    }

    public AiStatus Status
    {
        get
        {
            lock (_gate)
            {
                if (_client is null)
                {
                    return _availability switch
                    {
                        AiAvailability.InvalidAddress => AiStatus.InvalidAddress,
                        AiAvailability.InsecureAddress => AiStatus.InsecureAddress,
                        _ => AiStatus.NotConfigured,
                    };
                }

                if (!_enabled)
                {
                    return AiStatus.Disabled;
                }

                if (_keyRefused)
                {
                    return AiStatus.InvalidKey;
                }

                if (_time.GetUtcNow() < _pausedUntil)
                {
                    return _lastWasNoText ? AiStatus.NoText : AiStatus.Paused;
                }

                if (_inFlight)
                {
                    return AiStatus.Working;
                }

                return _lastWasNoText ? AiStatus.NoText : AiStatus.Ready;
            }
        }
    }

    /// <summary>A task that completes when no request is in flight. For tests.</summary>
    public Task WhenIdleAsync()
    {
        lock (_gate)
        {
            return _pending;
        }
    }

    /// <summary>A new presentation: the call counter and the texts start from scratch. The breaker state is kept.</summary>
    public void ResetSession()
    {
        lock (_gate)
        {
            _calls = 0;
            _cache.Clear();
            _cacheOrder.Clear();
            _requestedSequences.Clear();
            _activeSequence = null;
            _live = null;
            _lastRequestedKey = null;
            _lastCallAt = null;
        }

        RaiseChanged();
    }

    /// <summary>
    /// The recommendation of <paramref name="snapshot"/> with <see cref="Recommendation.FreeText"/> filled when a valid text
    /// exists for the current situation, otherwise unchanged. Never waits, never throws.
    /// </summary>
    public Recommendation? Apply(SessionSnapshot snapshot)
    {
        var recommendation = snapshot.Recommendation;
        var alert = snapshot.ActiveAlert;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            TrackSequence(alert?.Sequence, now);
            if (recommendation is null || alert is null || !_enabled || _client is null)
            {
                return recommendation;
            }

            var code = recommendation.Code.ToString();
            var kind = alert.Kind.ToString();
            var situation = AiSituation.Create(
                code,
                kind,
                snapshot.PaceStatus.ToString(),
                snapshot.SlidesRemaining,
                snapshot.RemainingMs / 1000,
                snapshot.TotalSlides,
                snapshot.PlannedDurationMs / 1000);
            var key = situation.Key;
            if (TryGetCached(key, now, out var text))
            {
                return recommendation with { FreeText = text };
            }

            // The key holds rounded minutes, so it can change while a slow answer is on its way. The text still belongs to
            // the alert it was asked for, as long as that alert (and its recommendation) is the one on screen.
            // A request for the new figures may still start (same guards as always); until it answers, the text stays.
            TryStart(situation, key, new RequestContext(alert.Sequence, code, kind, now, TimeSpan.Zero));
            if (_live is { } live && live.Sequence == alert.Sequence && live.Code == code && live.Kind == kind)
            {
                return recommendation with { FreeText = live.Text };
            }

            return recommendation;
        }
    }

    // Call with the lock held.
    private void TrackSequence(int? sequence, DateTimeOffset now)
    {
        if (sequence == _activeSequence)
        {
            return;
        }

        _activeSequence = sequence;
        _activeSeenAt = now;
        if (_live is not null && _live.Sequence != sequence)
        {
            _live = null;
        }
    }

    // Call with the lock held.
    private bool TryGetCached(string key, DateTimeOffset now, out string text)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (now - entry.StoredAt <= _options.CacheTimeToLive)
            {
                text = entry.Text;
                return true;
            }

            // Expired: forget it, and allow a new request for this situation.
            _cache.Remove(key);
            if (string.Equals(_lastRequestedKey, key, StringComparison.Ordinal))
            {
                _lastRequestedKey = null;
            }
        }

        text = string.Empty;
        return false;
    }

    // Call with the lock held.
    private void TryStart(AiSituation situation, string key, RequestContext context)
    {
        var now = context.StartedAt;
        // The first request of an alert sequence starts at once, whatever happened before: the alert is waiting for it.
        // Every other guard (one in flight, the cap, the breaker pause) still applies and still counts it.
        var firstOfSequence = !_requestedSequences.Contains(context.Sequence);
        if (_inFlight
            || (!firstOfSequence && string.Equals(key, _lastRequestedKey, StringComparison.Ordinal))
            || _calls >= _options.MaxCallsPerSession
            || now < _pausedUntil
            || (!firstOfSequence && _lastCallAt is { } last && now - last < _options.MinInterval))
        {
            return;
        }

        _inFlight = true;
        _requestedSequences.Add(context.Sequence);
        _lastRequestedKey = key;
        _lastCallAt = now;
        _calls++;
        var started = context with { AlertToRequest = _activeSequence == context.Sequence ? now - _activeSeenAt : TimeSpan.Zero };
        _pending = Task.Run(() => RunAsync(situation, key, started));
    }

    private async Task RunAsync(AiSituation situation, string key, RequestContext context)
    {
        RaiseChanged(); // "working"
        AiTextResult result;
        try
        {
            using var timeout = new CancellationTokenSource(_options.RequestTimeout, _time);
            result = await _client!.GenerateAsync(AiPromptBuilder.Build(situation), timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = AiTextResult.Fail(AiFailureKind.Timeout);
        }
        catch (Exception ex)
        {
            // Only the type is logged: the message of a network exception could name the address.
            _log.LogWarning("The AI client failed unexpectedly ({Type}).", ex.GetType().Name);
            result = AiTextResult.Fail(AiFailureKind.Network);
        }

        var newText = false;
#if DEBUG
        var duringAlert = false; // only read by the timing log below
#endif
        var outcome = "Accepted";
        TimeSpan duration;
        lock (_gate)
        {
            _inFlight = false;
            var finished = _time.GetUtcNow();
            duration = finished - context.StartedAt;
            var text = result.Succeeded ? AiTextSanitizer.Sanitize(result.Text) : null;
            if (text is not null)
            {
                // Always kept for reuse; shown only if the alert it was asked for is still the active one (ADR-81).
                Remember(key, text, finished);
                _lastText = text;
                _lastLatency = duration;
                if (_activeSequence == context.Sequence)
                {
                    _live = new LiveText(context.Sequence, context.Code, context.Kind, text);
#if DEBUG
                    duringAlert = true;
#endif
                }

                _consecutiveFailures = 0;
                _rateLimitHits = 0;
                _keyRefused = false;
                _lastWasNoText = false;
                newText = true;
                _log.LogInformation("AI request finished: accepted.");
            }
            else
            {
                var kind = result.Failure ?? AiFailureKind.Malformed; // an unusable text counts as a malformed answer
                outcome = kind.ToString();
                _lastWasNoText = kind == AiFailureKind.NoText;
                OnFailure(kind, result.RetryAfter);
            }
        }

#if DEBUG
        // Timing only: no prompt, no answer, no key. The code is the recommendation code, not text.
        _log.LogInformation(
            "PresentaTiming ai sequence={Sequence} code={Code} alertToRequestMs={AlertToRequestMs} requestMs={RequestMs} attempts={Attempts} outcome={Outcome} duringAlert={DuringAlert}",
            context.Sequence,
            context.Code,
            (long)context.AlertToRequest.TotalMilliseconds,
            (long)duration.TotalMilliseconds,
            result.Attempts,
            outcome,
            duringAlert);
#endif
        RaiseChanged();
        if (newText)
        {
            TextReady?.Invoke(this, EventArgs.Empty);
        }
    }

    // Call with the lock held.
    private void OnFailure(AiFailureKind kind, TimeSpan? retryAfter)
    {
        var now = _time.GetUtcNow();
        _consecutiveFailures++;
        var pause = TimeSpan.Zero;
        var breakerOpened = false;
        switch (kind)
        {
            case AiFailureKind.Unauthorized:
                _keyRefused = true;
                pause = _options.PauseDuration; // retrying a refused key at once is pointless
                breakerOpened = true;
                break;
            case AiFailureKind.RateLimited:
                _rateLimitHits++;
                var backoff = TimeSpan.FromTicks(_options.RateLimitBackoffInitial.Ticks << Math.Min(_rateLimitHits - 1, 20));
                pause = Min(_options.RateLimitBackoffMax, Max(backoff, retryAfter ?? TimeSpan.Zero));
                break;
        }

        if (_consecutiveFailures >= _options.FailureThreshold)
        {
            pause = Max(pause, _options.PauseDuration);
            breakerOpened = true;
        }

        if (pause > TimeSpan.Zero)
        {
            _pausedUntil = now + pause;
            if (breakerOpened)
            {
                // After the breaker's pause one trial request decides: a single further failure opens it again.
                // (A pause that is only a 429 backoff leaves the count alone.)
                _consecutiveFailures = Math.Max(0, _options.FailureThreshold - 1);
            }
        }

        _log.LogInformation("AI request finished: {Outcome}; paused for {Seconds} s.", kind, (long)pause.TotalSeconds);
    }

    // Call with the lock held.
    private void Remember(string key, string text, DateTimeOffset now)
    {
        _cache[key] = new CacheEntry(text, now);
        if (!_cacheOrder.Contains(key))
        {
            _cacheOrder.Enqueue(key);
            while (_cacheOrder.Count > _options.CacheSize)
            {
                _cache.Remove(_cacheOrder.Dequeue());
            }
        }
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A status handler failed.");
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a <= b ? a : b;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;
}
