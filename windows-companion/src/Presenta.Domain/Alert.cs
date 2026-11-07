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
public sealed record Alert(
    AlertKind Kind,
    AlertSeverity Severity,
    long RaisedAtElapsedMs,
    long RemainingMs,
    int SlidesRemaining,
    int SlideDeviation);
