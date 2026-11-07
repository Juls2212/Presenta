namespace Presenta.Domain;

internal readonly record struct AlertInput(
    TimeSpan Elapsed,
    TimeSpan Planned,
    int CurrentSlide,
    int TotalSlides,
    PaceResult Pace);

/// <summary>
/// Tracks which alerts have fired and which one is active. Rules:
/// time alerts fire once each (the most severe crossed threshold wins when several are crossed at once);
/// pace alerts fire on a transition into an off-pace status and may repeat after the cooldown only if the
/// presenter is still off pace and the previous alert was acknowledged; pace alerts are suppressed once the
/// critical-time threshold is reached; a new alert replaces the active one.
/// </summary>
internal sealed class AlertEngine
{
    private bool _runningOutFired;
    private bool _criticalFired;
    private bool _overtimeFired;
    private PaceStatus? _lastObservedPace;
    private PaceStatus? _lastPaceAlertStatus;
    private TimeSpan? _lastPaceAlertAt;

    public Alert? Active { get; private set; }

    public void Acknowledge() => Active = null;

    public void Clear() => Active = null;

    /// <summary>Evaluates the rules and returns the alert raised by this evaluation, if any.</summary>
    public Alert? Evaluate(in AlertInput input, SessionOptions options)
    {
        var remaining = input.Planned - input.Elapsed;
        var runningOut = options.GetTimeRunningOutThreshold(input.Planned);
        var critical = options.GetTimeCriticalThreshold(input.Planned);
        var slidesRemaining = input.TotalSlides - input.CurrentSlide;
        var deviation = (int)Math.Round(input.Pace.Deviation, MidpointRounding.AwayFromZero);
        var elapsed = input.Elapsed;

        Alert? raised = null;

        if (remaining < TimeSpan.Zero)
        {
            if (!_overtimeFired)
            {
                raised = Build(AlertKind.Overtime, AlertSeverity.Critical);
            }

            _overtimeFired = _criticalFired = _runningOutFired = true;
        }
        else if (remaining <= critical)
        {
            if (!_criticalFired)
            {
                raised = Build(AlertKind.TimeCritical, AlertSeverity.Critical);
            }

            _criticalFired = _runningOutFired = true;
        }
        else if (remaining <= runningOut)
        {
            if (!_runningOutFired)
            {
                raised = Build(AlertKind.TimeRunningOut, AlertSeverity.Warning);
            }

            _runningOutFired = true;
        }

        if (raised is not null)
        {
            Active = raised;
        }

        var status = input.Pace.Status;
        var offPace = status is PaceStatus.Behind or PaceStatus.TooFast;
        var suppressed = remaining <= critical;

        if (offPace)
        {
            var transition = status != _lastObservedPace;
            var repeat = !transition
                && _lastPaceAlertAt is { } lastAt
                && _lastPaceAlertStatus == status
                && Active is null
                && elapsed - lastAt >= options.PaceAlertCooldown;

            // A time alert raised in this same evaluation takes precedence; the pace transition is consumed.
            if (!suppressed && raised is null && (transition || repeat))
            {
                var kind = status == PaceStatus.Behind ? AlertKind.BehindSchedule : AlertKind.TooFast;
                raised = Build(kind, AlertSeverity.Warning);
                Active = raised;
                _lastPaceAlertAt = elapsed;
                _lastPaceAlertStatus = status;
            }
        }
        else if (Active is { Kind: AlertKind.BehindSchedule or AlertKind.TooFast })
        {
            // The presenter recovered; a pace alert is no longer relevant.
            Active = null;
        }

        _lastObservedPace = status;
        return raised;

        Alert Build(AlertKind kind, AlertSeverity severity) => new(
            kind,
            severity,
            ToMs(elapsed),
            ToMs(remaining),
            slidesRemaining,
            deviation);
    }

    internal static long ToMs(TimeSpan value) => (long)Math.Floor(value.TotalMilliseconds);
}
