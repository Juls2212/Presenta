using Presenta.Domain.Keyboard;

namespace Presenta.Targeting;

/// <summary>What the companion knows about one top-level window. Handles are plain numbers so the type stays portable.</summary>
/// <param name="Handle">The window handle (HWND) as a number.</param>
/// <param name="Title">The window title; may be empty.</param>
/// <param name="ClassName">The window class name.</param>
/// <param name="ProcessId">Owning process id.</param>
/// <param name="ProcessName">Image name of the owning process without the extension, for example <c>POWERPNT</c>.</param>
/// <param name="IsVisible">The window is shown (a minimized window is still visible in this sense).</param>
/// <param name="IsCloaked">The window is hidden by the shell (for example on another virtual desktop).</param>
/// <param name="IsToolWindow">The window has the tool-window style.</param>
/// <param name="IsMinimized">The window is minimized.</param>
/// <param name="IsElevated">The owning process runs elevated (or could not be inspected).</param>
public sealed record WindowInfo(
    long Handle,
    string Title,
    string ClassName,
    int ProcessId,
    string ProcessName,
    bool IsVisible = true,
    bool IsCloaked = false,
    bool IsToolWindow = false,
    bool IsMinimized = false,
    bool IsElevated = false);

/// <summary>More about one window, for the diagnostic report and the foreground rule.</summary>
/// <param name="Info">The basic description.</param>
/// <param name="IsMaximized">The window is maximized.</param>
/// <param name="IsTopMost">The window has the top-most style.</param>
/// <param name="Owner">Handle of the window's owner (not its parent), or 0.</param>
/// <param name="RootOwner">Handle of the root owner of the window, or 0.</param>
public sealed record WindowDetails(WindowInfo Info, bool IsMaximized, bool IsTopMost, long Owner, long RootOwner);

/// <summary>What happened in one attempt to bring a window to the front. Error values are Win32 last-error codes (0 when none).</summary>
public sealed record ForegroundAttempt
{
    public long ForegroundBefore { get; init; }

    public int ForegroundThread { get; init; }

    public int CurrentThread { get; init; }

    public bool Attached { get; init; }

    public int AttachError { get; init; }

    public bool SetForegroundSucceeded { get; init; }

    public int SetForegroundError { get; init; }

    public bool BringToTopSucceeded { get; init; }

    public int BringToTopError { get; init; }

    public bool Detached { get; init; }

    public int DetachError { get; init; }
}

/// <summary>
/// Everything the companion needs from the window system. The Windows implementation lives in
/// <c>Presenta.Keyboard.Windows</c> (the only place that calls Win32); tests supply a fake.
/// </summary>
public interface IWindowSystem
{
    /// <summary>Id of the companion's own process, whose windows are never offered as a target.</summary>
    int CurrentProcessId { get; }

    /// <summary>True when the companion runs elevated (so it can send input to elevated windows).</summary>
    bool IsCurrentProcessElevated { get; }

    /// <summary>All top-level windows, in any order, with no filtering.</summary>
    IReadOnlyList<WindowInfo> EnumerateTopLevelWindows();

    /// <summary>The window with this handle, or null when it no longer exists.</summary>
    WindowInfo? GetWindow(long handle);

    /// <summary>Handle of the current foreground window, or 0.</summary>
    long GetForegroundWindow();

    /// <summary>Restores a minimized window. Does not change the maximized or full-screen state of other windows.</summary>
    bool Restore(long handle);

    /// <summary>
    /// Asks Windows to bring the window to the foreground using the supported technique for a caller that is not itself
    /// the foreground process (attach to the foreground thread's input, set the foreground window, bring it to the top,
    /// detach). Returns what each step reported; the caller must still confirm with <see cref="WaitUntilForeground"/>.
    /// </summary>
    ForegroundAttempt BringToForeground(long handle);

    /// <summary>Waits up to <paramref name="timeout"/> for the window, or a window <paramref name="alsoAccept"/> approves, to become the foreground window.</summary>
    bool WaitUntilForeground(long handle, TimeSpan timeout, Func<long, bool>? alsoAccept = null);

    /// <summary>Handle of the owner of the window (not its parent), or 0 when it has none or does not exist.</summary>
    long GetOwner(long handle);

    /// <summary>Extra details of the window, or null when it no longer exists.</summary>
    WindowDetails? GetDetails(long handle);

    /// <summary>Presses and releases the Right (Next) or Left (Previous) arrow key into whatever window is in the foreground.</summary>
    bool SendArrow(SlideDirection direction);
}
