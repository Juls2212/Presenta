namespace Presenta.Targeting;

/// <summary>How the window that is in the foreground relates to the chosen presentation window.</summary>
public enum ForegroundRelation
{
    /// <summary>There is no foreground window, or it no longer exists.</summary>
    Unknown,

    /// <summary>The foreground window is exactly the chosen window.</summary>
    Target,

    /// <summary>The foreground window is owned (directly or through other owned windows) by the chosen window, all in the same program.</summary>
    OwnedByTarget,

    /// <summary>The foreground window owns the chosen window (the other direction). Not accepted.</summary>
    OwnerOfTarget,

    /// <summary>Another window of the same program with no owner relationship to the chosen window. Not accepted.</summary>
    SameProcessOther,

    /// <summary>A window of a different program. Never accepted.</summary>
    OtherProcess,
}

/// <summary>One attempt to bring the target to the front, as seen by the diagnostic report.</summary>
/// <param name="Attempt">What each Win32 step reported.</param>
/// <param name="Confirmed">Whether an accepted window became the foreground window within the confirmation time.</param>
/// <param name="ForegroundAfter">The foreground window after the confirmation wait.</param>
/// <param name="Relation">How that window relates to the target.</param>
public sealed record ActivationAttemptTrace(ForegroundAttempt Attempt, bool Confirmed, WindowDetails? ForegroundAfter, ForegroundRelation Relation);

/// <summary>
/// Everything that happened during one activation, collected for the diagnostic report. Passing a trace to
/// <see cref="PresentationTargetService.Activate"/> changes nothing about what the activation does.
/// </summary>
public sealed class ActivationTrace
{
    public WindowDetails? Target { get; set; }

    public WindowDetails? ForegroundBefore { get; set; }

    public ForegroundRelation RelationBefore { get; set; }

    public bool RestoreAttempted { get; set; }

    public bool RestoreResult { get; set; }

    public List<ActivationAttemptTrace> Attempts { get; } = new();

    public ActivationOutcome Outcome { get; set; }
}
