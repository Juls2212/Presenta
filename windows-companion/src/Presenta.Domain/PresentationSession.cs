using Presenta.Domain.Keyboard;

namespace Presenta.Domain;

/// <summary>
/// The session state machine: timer, slide counter, pace, alerts, recommendations and summary.
/// Pure logic: no I/O and no wall-clock access other than the injected <see cref="TimeProvider"/>.
/// <para>
/// Threading: not thread-safe. The caller (the companion) must serialize access.
/// </para>
/// <para>
/// Tick contract: time-based alerts (time running out, time critical, overtime, and pace alerts caused by the
/// passing of time) are only evaluated when the session is touched. The companion must therefore call
/// <see cref="Tick"/> periodically (recommended: once per second) while a session is running, and broadcast a
/// new snapshot whenever <see cref="Tick"/> returns an alert.
/// </para>
/// </summary>
public sealed class PresentationSession
{
    private readonly TimeProvider _time;
    private readonly SessionOptions _options;
    private readonly IRecommendationProvider _recommendations;
    private readonly AlertEngine _alerts = new();

    private SessionState _state = SessionState.Configured;
    private int _currentSlide = 1;
    private TimeSpan _accumulated = TimeSpan.Zero;
    private long _runStartTimestamp;
    private FinalState? _finalState;
    private DateTimeOffset? _finishedAt;

    public PresentationSession(
        SessionConfiguration configuration,
        TimeProvider time,
        SessionOptions? options = null,
        IRecommendationProvider? recommendations = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(time);
        Configuration = configuration;
        _time = time;
        _options = options ?? new SessionOptions();
        _recommendations = recommendations ?? new RuleBasedRecommendationProvider();
    }

    public SessionConfiguration Configuration { get; }

    public SessionState State => _state;

    public int CurrentSlide => _currentSlide;

    private TimeSpan Elapsed =>
        _state == SessionState.Running ? _accumulated + _time.GetElapsedTime(_runStartTimestamp) : _accumulated;

    /// <summary>Configured to running. Elapsed time starts counting.</summary>
    public OperationResult Start()
    {
        if (_state != SessionState.Configured)
        {
            return OperationResult.Failure(SessionErrorCode.InvalidState);
        }

        _state = SessionState.Running;
        _runStartTimestamp = _time.GetTimestamp();
        EvaluateAlerts();
        return OperationResult.Success;
    }

    /// <summary>Running to paused. Elapsed time stops counting.</summary>
    public OperationResult Pause()
    {
        if (_state != SessionState.Running)
        {
            return OperationResult.Failure(SessionErrorCode.InvalidState);
        }

        _accumulated = Elapsed;
        _state = SessionState.Paused;
        return OperationResult.Success;
    }

    /// <summary>Paused to running.</summary>
    public OperationResult Resume()
    {
        if (_state != SessionState.Paused)
        {
            return OperationResult.Failure(SessionErrorCode.InvalidState);
        }

        _state = SessionState.Running;
        _runStartTimestamp = _time.GetTimestamp();
        EvaluateAlerts();
        return OperationResult.Success;
    }

    /// <summary>Moves one slide forward. At the last slide the counter does not change (and the call succeeds).</summary>
    public SlideResult Next() => MoveTo(_currentSlide + 1, clamp: true);

    /// <summary>Moves one slide back. At slide 1 the counter does not change (and the call succeeds).</summary>
    public SlideResult Previous() => MoveTo(_currentSlide - 1, clamp: true);

    /// <summary>
    /// True when <see cref="Next"/> or <see cref="Previous"/> would actually move the counter right now. It changes nothing.
    /// The companion uses it to deliver the key first and commit the counter only if the key was delivered.
    /// </summary>
    public bool WouldMove(SlideDirection direction)
    {
        if (_state is not (SessionState.Running or SessionState.Paused))
        {
            return false;
        }

        var target = direction == SlideDirection.Next ? _currentSlide + 1 : _currentSlide - 1;
        return target >= 1 && target <= Configuration.PlannedSlides;
    }

    /// <summary>Sets the counter manually. Out-of-range values fail with <see cref="SessionErrorCode.SlideOutOfRange"/>.</summary>
    public SlideResult SetSlide(int slide) => MoveTo(slide, clamp: false);

    /// <summary>Running to finished (finishedManually), or paused to finished (endedWhilePaused).</summary>
    public OperationResult Finish() => _state switch
    {
        SessionState.Running => End(FinalState.FinishedManually),
        SessionState.Paused => End(FinalState.EndedWhilePaused),
        _ => OperationResult.Failure(SessionErrorCode.InvalidState),
    };

    /// <summary>
    /// Ends a running or paused session as interrupted by disconnection. Elapsed time counts until this call,
    /// so it includes any grace period during which the session kept running.
    /// </summary>
    public OperationResult MarkInterrupted() =>
        _state is SessionState.Running or SessionState.Paused
            ? End(FinalState.InterruptedByDisconnection)
            : OperationResult.Failure(SessionErrorCode.InvalidState);

    /// <summary>Clears the active alert. Harmless when there is none, and in any state.</summary>
    public void AcknowledgeAlert() => _alerts.Acknowledge();

    /// <summary>
    /// Evaluates time-based alerts. Returns the alert raised by this call, or null. Does nothing unless running.
    /// </summary>
    public Alert? Tick() => _state == SessionState.Running ? EvaluateAlerts() : null;

    public SessionSnapshot GetSnapshot()
    {
        var elapsed = Elapsed;
        var planned = Configuration.PlannedDuration;
        var remaining = planned - elapsed;
        var total = Configuration.PlannedSlides;
        var pace = PaceCalculator.Evaluate(_options, _currentSlide, total, elapsed, planned);
        var slidesRemaining = total - _currentSlide;

        var recommendation = _recommendations.Recommend(new RecommendationContext(
            _state,
            pace.Status,
            pace.Deviation,
            pace.Tolerance,
            slidesRemaining,
            AlertEngine.ToMs(remaining),
            AlertEngine.ToMs(_options.GetTimeCriticalThreshold(planned))));

        return new SessionSnapshot(
            _state,
            Configuration.Name,
            _currentSlide,
            total,
            AlertEngine.ToMs(planned),
            AlertEngine.ToMs(elapsed),
            AlertEngine.ToMs(remaining),
            _state == SessionState.Running,
            _time.GetUtcNow(),
            (double)_currentSlide / total,
            pace.Status,
            slidesRemaining,
            PaceCalculator.AverageSlidesPerMinute(_options, _currentSlide, elapsed),
            _finalState,
            _alerts.Active,
            recommendation);
    }

    /// <summary>Builds the summary of a finished session. Fails with InvalidState before the session ends.</summary>
    public Result<SessionSummary> BuildSummary()
    {
        if (_state != SessionState.Finished || _finalState is null || _finishedAt is null)
        {
            return Result<SessionSummary>.Failure(SessionErrorCode.InvalidState);
        }

        var elapsed = _accumulated;
        return Result<SessionSummary>.Success(new SessionSummary(
            Configuration.Name,
            _finishedAt.Value,
            AlertEngine.ToMs(Configuration.PlannedDuration),
            AlertEngine.ToMs(elapsed),
            Configuration.PlannedSlides,
            _currentSlide,
            PaceCalculator.AverageSlidesPerMinute(_options, _currentSlide, elapsed),
            _finalState.Value,
            elapsed <= Configuration.PlannedDuration));
    }

    private SlideResult MoveTo(int target, bool clamp)
    {
        if (_state is not (SessionState.Running or SessionState.Paused))
        {
            return SlideResult.Failure(SessionErrorCode.InvalidState, _currentSlide);
        }

        var total = Configuration.PlannedSlides;
        if (clamp)
        {
            target = Math.Clamp(target, 1, total);
        }
        else if (target < 1 || target > total)
        {
            return SlideResult.Failure(SessionErrorCode.SlideOutOfRange, _currentSlide);
        }

        var changed = target != _currentSlide;
        _currentSlide = target;
        if (changed && _state == SessionState.Running)
        {
            EvaluateAlerts();
        }

        return SlideResult.Ok(changed, _currentSlide);
    }

    private OperationResult End(FinalState finalState)
    {
        _accumulated = Elapsed;
        _state = SessionState.Finished;
        _finalState = finalState;
        _finishedAt = _time.GetUtcNow();
        _alerts.Clear();
        return OperationResult.Success;
    }

    private Alert? EvaluateAlerts()
    {
        var elapsed = Elapsed;
        var pace = PaceCalculator.Evaluate(
            _options, _currentSlide, Configuration.PlannedSlides, elapsed, Configuration.PlannedDuration);
        return _alerts.Evaluate(
            new AlertInput(elapsed, Configuration.PlannedDuration, _currentSlide, Configuration.PlannedSlides, pace),
            _options);
    }
}
