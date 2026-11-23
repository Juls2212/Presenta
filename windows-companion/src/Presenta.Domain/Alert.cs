namespace Presenta.Domain;

/// <summary>
/// An alert with structured parameters only (no display text).
/// </summary>
/// <param name="Kind">Alert kind.</param>
/// <param name="Severity">Alert severity.</param>
/// <param name="RaisedAtElapsedMs">Session elapsed time when the alert was raised.</param>
/// <param name="RemainingMs">Remaining time when raised (negative in overtime).</param>
/// <param name="SlidesRemaining">Slides left after the current one when raised.</param>
/// <param name="SlideDeviation">Actual minus expected slide, rounded; negative means behind.</param>
/// <param name="Sequence">Identity of this alert instance: 1 for the first alert of the session, then 2, 3, and so on.
/// A pace alert that fires again after the cooldown gets a new number even though its kind is the same.</param>
public sealed record Alert(
    AlertKind Kind,
    AlertSeverity Severity,
    long RaisedAtElapsedMs,
    long RemainingMs,
    int SlidesRemaining,
    int SlideDeviation,
    int Sequence);
