using Presenta.Domain.Keyboard;
using Presenta.Targeting;

namespace Presenta.TestSupport;

/// <summary>What the fake does when asked to bring a window to the foreground.</summary>
public enum ForegroundBehavior
{
    /// <summary>The window becomes the foreground window and the call reports success.</summary>
    Succeed,

    /// <summary>Windows refuses: the call reports failure and the foreground does not change.</summary>
    Refuse,

    /// <summary>The call reports success but the foreground never changes (the foreground lock won).</summary>
    AcceptButStayBehind,
}

/// <summary>
/// An in-memory window system. It records where every key lands (the foreground window at the moment of the key), so tests
/// can prove that keys reach the intended window or nobody.
/// </summary>
public sealed class FakeWindowSystem : IWindowSystem
{
    public const long ThiefHandle = 900;

    private readonly object _gate = new();
    private readonly List<WindowInfo> _windows = new();
    private readonly List<string> _calls = new();
    private readonly List<(long Window, SlideDirection Direction)> _keys = new();
    private long _foreground;

    public int CurrentProcessId { get; set; } = 1000;

    public bool IsCurrentProcessElevated { get; set; }

    public ForegroundBehavior Behavior { get; set; } = ForegroundBehavior.Succeed;

    public bool SendArrowResult { get; set; } = true;

    /// <summary>Runs right after a foreground confirmation, to simulate the user clicking elsewhere before the key.</summary>
    public Action<FakeWindowSystem>? AfterConfirmation { get; set; }

    /// <summary>When set, the foreground jumps to this window right after a successful activation (a window stealing focus back).</summary>
    public long? StealForegroundAfterActivation { get; set; }

    public long Foreground
    {
        get
        {
            lock (_gate)
            {
                return _foreground;
            }
        }
        set
        {
            lock (_gate)
            {
                _foreground = value;
            }
        }
    }

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    /// <summary>Each key with the handle of the window that was in the foreground when it was pressed.</summary>
    public IReadOnlyList<(long Window, SlideDirection Direction)> Keys
    {
        get
        {
            lock (_gate)
            {
                return _keys.ToArray();
            }
        }
    }

    public FakeWindowSystem Add(WindowInfo window)
    {
        lock (_gate)
        {
            _windows.Add(window);
        }

        return this;
    }

    public void Remove(long handle)
    {
        lock (_gate)
        {
            _windows.RemoveAll(w => w.Handle == handle);
        }
    }

    public void Update(long handle, Func<WindowInfo, WindowInfo> change)
    {
        lock (_gate)
        {
            var index = _windows.FindIndex(w => w.Handle == handle);
            _windows[index] = change(_windows[index]);
        }
    }

    public IReadOnlyList<WindowInfo> EnumerateTopLevelWindows()
    {
        lock (_gate)
        {
            _calls.Add("enumerate");
            return _windows.ToArray();
        }
    }

    public WindowInfo? GetWindow(long handle)
    {
        lock (_gate)
        {
            return _windows.FirstOrDefault(w => w.Handle == handle);
        }
    }

    public long GetForegroundWindow() => Foreground;

    public bool Restore(long handle)
    {
        lock (_gate)
        {
            _calls.Add("restore " + handle);
            var index = _windows.FindIndex(w => w.Handle == handle);
            if (index < 0)
            {
                return false;
            }

            _windows[index] = _windows[index] with { IsMinimized = false };
            return true;
        }
    }

    public bool TryBringToForeground(long handle)
    {
        lock (_gate)
        {
            _calls.Add("activate " + handle);
            switch (Behavior)
            {
                case ForegroundBehavior.Refuse:
                    return false;
                case ForegroundBehavior.AcceptButStayBehind:
                    return true;
                default:
                    _foreground = StealForegroundAfterActivation ?? handle;
                    return true;
            }
        }
    }

    public bool WaitUntilForeground(long handle, TimeSpan timeout)
    {
        bool confirmed;
        lock (_gate)
        {
            _calls.Add("wait " + handle);
            confirmed = _foreground == handle;
        }

        if (confirmed)
        {
            AfterConfirmation?.Invoke(this);
        }

        return confirmed;
    }

    public bool SendArrow(SlideDirection direction)
    {
        lock (_gate)
        {
            _calls.Add("key " + direction);
            if (!SendArrowResult)
            {
                return false;
            }

            _keys.Add((_foreground, direction));
            return true;
        }
    }

    public static WindowInfo Window(
        long handle,
        string title,
        string processName = "app",
        string className = "AppClass",
        int processId = 2000,
        bool minimized = false,
        bool elevated = false) =>
        new(handle, title, className, processId, processName, IsMinimized: minimized, IsElevated: elevated);
}
