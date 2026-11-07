using Presenta.Domain.Keyboard;

namespace Presenta.Targeting;

/// <summary>A window the user may choose, and whether it is the suggested one (a PowerPoint slide-show window).</summary>
public sealed record TargetCandidate(WindowInfo Window, bool IsSuggested);

/// <summary>The chosen window resolved against what is open right now.</summary>
public sealed record TargetResolution(TargetState State, WindowInfo? Window);

/// <summary>Result of bringing the target to the front.</summary>
public enum ActivationOutcome
{
    /// <summary>The target is confirmed to be the foreground window.</summary>
    InForeground,
    NoTargetSelected,
    TargetNotFound,
    TargetAmbiguous,
    TargetElevated,
    ActivationFailed,
    NotConfirmedForeground,
}

/// <summary>Outcome of <see cref="PresentationTargetService.Activate"/> plus the window that was activated.</summary>
public sealed record ActivationResult(ActivationOutcome Outcome, WindowInfo? Window)
{
    public bool IsInForeground => Outcome == ActivationOutcome.InForeground;
}

/// <summary>How hard to try to bring the target to the front. The worst case is <c>Attempts x ConfirmTimeout</c>.</summary>
public sealed record ActivationOptions
{
    public int Attempts { get; init; } = 3;

    public TimeSpan ConfirmTimeout { get; init; } = TimeSpan.FromMilliseconds(250);
}

/// <summary>
/// Lists candidate windows, remembers the chosen one (as an identity, saved to disk), resolves it to a live window,
/// reports its status and brings it to the front. It never presses a key.
/// Thread-safe: the lock only guards the choice; Windows calls and waits run outside it.
/// </summary>
public sealed class PresentationTargetService
{
    /// <summary>Program image name of PowerPoint.</summary>
    public const string SuggestedProcessName = "POWERPNT";

    /// <summary>Window class of a PowerPoint slide show, windowed or full screen. The editing window is a different class.</summary>
    public const string SuggestedWindowClass = "screenClass";

    private readonly IWindowSystem _windows;
    private readonly ITargetSettingsStore _store;
    private readonly ActivationOptions _options;
    private readonly object _gate = new();

    private PresentationTargetIdentity? _identity;
    private long _cachedHandle;

    public PresentationTargetService(IWindowSystem windows, ITargetSettingsStore store, ActivationOptions? options = null)
    {
        _windows = windows;
        _store = store;
        _options = options ?? new ActivationOptions();
        _identity = store.Load();
    }

    /// <summary>Raised after the choice changes (selected or cleared). May run on any thread.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The saved choice, or null when nothing is selected.</summary>
    public PresentationTargetIdentity? Identity
    {
        get
        {
            lock (_gate)
            {
                return _identity;
            }
        }
    }

    /// <summary>Visible, uncloaked, titled, non-tool windows of other programs; suggested ones first.</summary>
    public IReadOnlyList<TargetCandidate> ListCandidates() =>
        _windows.EnumerateTopLevelWindows()
            .Where(IsCandidate)
            .Select(w => new TargetCandidate(w, IsSuggested(w)))
            .OrderByDescending(c => c.IsSuggested)
            .ThenBy(c => c.Window.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Window.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Chooses <paramref name="window"/> and saves the choice. Returns false (and chooses nothing) if the window is not a valid candidate.</summary>
    public bool Select(WindowInfo window)
    {
        if (!IsCandidate(window))
        {
            return false;
        }

        lock (_gate)
        {
            _identity = PresentationTargetIdentity.From(window);
            _cachedHandle = window.Handle;
            _store.Save(_identity);
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _identity = null;
            _cachedHandle = 0;
            _store.Save(null);
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Resolves the choice to a live window and reports its state. Changes nothing on screen.</summary>
    public TargetResolution Resolve()
    {
        PresentationTargetIdentity? identity;
        long cached;
        lock (_gate)
        {
            identity = _identity;
            cached = _cachedHandle;
        }

        if (identity is null)
        {
            return new TargetResolution(TargetState.NoneSelected, null);
        }

        // 1. The remembered window, if it is still alive and still looks right.
        if (cached != 0 && _windows.GetWindow(cached) is { } known && IsCandidate(known) && identity.Matches(known))
        {
            return Classify(known);
        }

        // 2. Otherwise look for it again among the open windows.
        var matches = _windows.EnumerateTopLevelWindows().Where(w => IsCandidate(w) && identity.Matches(w)).ToList();
        switch (matches.Count)
        {
            case 0:
                return new TargetResolution(TargetState.NotFound, null);
            case 1:
                lock (_gate)
                {
                    if (ReferenceEquals(_identity, identity))
                    {
                        _cachedHandle = matches[0].Handle;
                    }
                }

                return Classify(matches[0]);
            default:
                return new TargetResolution(TargetState.Ambiguous, null);
        }
    }

    public TargetState GetState() => Resolve().State;

    /// <summary>
    /// Restores the target if it is minimized, then brings it to the foreground unless it already is, and confirms that it
    /// really is the foreground window (a few short, bounded attempts). Presses no key. Focus is not given back afterwards.
    /// </summary>
    public ActivationResult Activate()
    {
        var resolution = Resolve();
        switch (resolution.State)
        {
            case TargetState.NoneSelected:
                return new ActivationResult(ActivationOutcome.NoTargetSelected, null);
            case TargetState.NotFound:
                return new ActivationResult(ActivationOutcome.TargetNotFound, null);
            case TargetState.Ambiguous:
                return new ActivationResult(ActivationOutcome.TargetAmbiguous, null);
            case TargetState.Elevated:
                return new ActivationResult(ActivationOutcome.TargetElevated, resolution.Window);
        }

        var window = resolution.Window!;
        if (resolution.State == TargetState.Minimized)
        {
            _windows.Restore(window.Handle);
        }

        if (_windows.GetForegroundWindow() == window.Handle)
        {
            return new ActivationResult(ActivationOutcome.InForeground, window);
        }

        var accepted = false;
        for (var attempt = 0; attempt < _options.Attempts; attempt++)
        {
            if (!_windows.TryBringToForeground(window.Handle))
            {
                continue;
            }

            accepted = true;
            if (_windows.WaitUntilForeground(window.Handle, _options.ConfirmTimeout))
            {
                return new ActivationResult(ActivationOutcome.InForeground, window);
            }
        }

        return new ActivationResult(accepted ? ActivationOutcome.NotConfirmedForeground : ActivationOutcome.ActivationFailed, window);
    }

    private TargetResolution Classify(WindowInfo window)
    {
        if (window.IsElevated && !_windows.IsCurrentProcessElevated)
        {
            return new TargetResolution(TargetState.Elevated, window);
        }

        return new TargetResolution(window.IsMinimized ? TargetState.Minimized : TargetState.Ready, window);
    }

    private bool IsCandidate(WindowInfo window) =>
        window.IsVisible
        && !window.IsCloaked
        && !window.IsToolWindow
        && !string.IsNullOrWhiteSpace(window.Title)
        && window.ProcessId != _windows.CurrentProcessId;

    private static bool IsSuggested(WindowInfo window) =>
        string.Equals(window.ProcessName, SuggestedProcessName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(window.ClassName, SuggestedWindowClass, StringComparison.Ordinal);
}
