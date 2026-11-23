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
}

/// <summary>Limits of the enricher. Every default is the documented value (ADR-76).</summary>
public sealed record AiEnrichmentOptions
{
    /// <summary>At most one request in this time.</summary>
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

    /// <summary>The enricher gives up on a request after this long, whatever the client does.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Texts kept by situation.</summary>
    public int CacheSize { get; init; } = 16;
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
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
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
    private int _calls;
    private string? _lastText;

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
                    return AiStatus.Paused;
                }

                return _inFlight ? AiStatus.Working : AiStatus.Ready;
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
        if (recommendation is null || snapshot.ActiveAlert is not { } alert)
        {
            return recommendation;
        }

        lock (_gate)
        {
            if (!_enabled || _client is null)
            {
                return recommendation;
            }

            var situation = AiSituation.Create(
                recommendation.Code.ToString(),
                alert.Kind.ToString(),
                snapshot.PaceStatus.ToString(),
                snapshot.SlidesRemaining,
                snapshot.RemainingMs / 1000,
                snapshot.TotalSlides,
                snapshot.PlannedDurationMs / 1000);
            var key = situation.Key;
            if (_cache.TryGetValue(key, out var text))
            {
                return recommendation with { FreeText = text };
            }

            TryStart(situation, key);
            return recommendation;
        }
    }

    // Call with the lock held.
    private void TryStart(AiSituation situation, string key)
    {
        var now = _time.GetUtcNow();
        if (_inFlight
            || string.Equals(key, _lastRequestedKey, StringComparison.Ordinal)
            || _calls >= _options.MaxCallsPerSession
            || now < _pausedUntil
            || (_lastCallAt is { } last && now - last < _options.MinInterval))
        {
            return;
        }

        _inFlight = true;
        _lastRequestedKey = key;
        _lastCallAt = now;
        _calls++;
        _pending = Task.Run(() => RunAsync(situation, key));
    }

    private async Task RunAsync(AiSituation situation, string key)
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
        lock (_gate)
        {
            _inFlight = false;
            var text = result.Succeeded ? AiTextSanitizer.Sanitize(result.Text) : null;
            if (text is not null)
            {
                Remember(key, text);
                _lastText = text;
                _consecutiveFailures = 0;
                _rateLimitHits = 0;
                _keyRefused = false;
                newText = true;
                _log.LogInformation("AI request finished: accepted.");
            }
            else
            {
                var kind = result.Failure ?? AiFailureKind.Malformed; // an unusable text counts as a malformed answer
                OnFailure(kind, result.RetryAfter);
            }
        }

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
    private void Remember(string key, string text)
    {
        if (_cache.TryAdd(key, text))
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
