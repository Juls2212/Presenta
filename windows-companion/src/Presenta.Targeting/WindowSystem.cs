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
    /// the foreground process (attach to the foreground thread's input, set the foreground window, detach).
    /// Returns what Windows reported; the caller must still confirm with <see cref="WaitUntilForeground"/>.
    /// </summary>
    bool TryBringToForeground(long handle);

    /// <summary>Waits up to <paramref name="timeout"/> for the window to become the foreground window.</summary>
    bool WaitUntilForeground(long handle, TimeSpan timeout);

    /// <summary>Presses and releases the Right (Next) or Left (Previous) arrow key into whatever window is in the foreground.</summary>
    bool SendArrow(SlideDirection direction);
}
