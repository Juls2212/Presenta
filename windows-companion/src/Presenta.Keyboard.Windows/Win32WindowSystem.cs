using System.Diagnostics;
using System.Runtime.InteropServices;
using Presenta.Domain.Keyboard;
using Presenta.Targeting;

namespace Presenta.Keyboard.Windows;

internal static unsafe partial class WindowNative
{
    internal const int GwlExStyle = -20;
    internal const int WsExToolWindow = 0x80;
    internal const int SwRestore = 9;
    internal const int DwmaCloaked = 14;
    internal const int ErrorAccessDenied = 5;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    internal static partial int GetWindowText(nint window, char* buffer, int maxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    internal static partial int GetClassName(nint window, char* buffer, int maxCount);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static partial int GetWindowLong(nint window, int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AttachThreadInput(uint attachThread, uint attachToThread, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTokenInformation(nint token, int informationClass, out int information, int length, out int returnLength);
}

/// <summary>
/// The Windows implementation of <see cref="IWindowSystem"/>. This is the only place that calls window-management Win32
/// functions. It sends no keys other than Right and Left (through <see cref="ArrowKeyInjector"/>), and it does not use
/// the Alt-key trick or any other synthetic input to get around the foreground lock.
/// </summary>
public sealed unsafe class Win32WindowSystem : IWindowSystem
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;

    public int CurrentProcessId { get; } = Environment.ProcessId;

    public bool IsCurrentProcessElevated { get; } = IsElevated((uint)Environment.ProcessId);

    public IReadOnlyList<WindowInfo> EnumerateTopLevelWindows()
    {
        var handles = new List<nint>();
        var state = GCHandle.Alloc(handles);
        try
        {
            WindowNative.EnumWindows(&CollectWindow, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        var elevationByProcess = new Dictionary<int, bool>();
        var nameByProcess = new Dictionary<int, string>();
        var windows = new List<WindowInfo>(handles.Count);
        foreach (var handle in handles)
        {
            if (Describe(handle, elevationByProcess, nameByProcess) is { } info)
            {
                windows.Add(info);
            }
        }

        return windows;
    }

    public WindowInfo? GetWindow(long handle) =>
        WindowNative.IsWindow((nint)handle) ? Describe((nint)handle, new Dictionary<int, bool>(), new Dictionary<int, string>()) : null;

    public long GetForegroundWindow() => WindowNative.GetForegroundWindow();

    public bool Restore(long handle) => WindowNative.IsIconic((nint)handle) && WindowNative.ShowWindow((nint)handle, WindowNative.SwRestore);

    public bool TryBringToForeground(long handle)
    {
        var foreground = WindowNative.GetForegroundWindow();
        var foregroundThread = foreground == 0 ? 0 : WindowNative.GetWindowThreadProcessId(foreground, out _);
        var thisThread = WindowNative.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != thisThread
            && WindowNative.AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            return WindowNative.SetForegroundWindow((nint)handle);
        }
        finally
        {
            if (attached)
            {
                WindowNative.AttachThreadInput(thisThread, foregroundThread, false);
            }
        }
    }

    public bool WaitUntilForeground(long handle, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        using var pause = new ManualResetEventSlim(false);
        while (true)
        {
            if (WindowNative.GetForegroundWindow() == (nint)handle)
            {
                return true;
            }

            if (clock.Elapsed >= timeout)
            {
                return false;
            }

            pause.Wait(TimeSpan.FromMilliseconds(10));
        }
    }

    public bool SendArrow(SlideDirection direction) => ArrowKeyInjector.Press(direction);

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static int CollectWindow(nint window, nint parameter)
    {
        if (GCHandle.FromIntPtr(parameter).Target is List<nint> list)
        {
            list.Add(window);
        }

        return 1;
    }

    private static WindowInfo? Describe(nint window, Dictionary<int, bool> elevationByProcess, Dictionary<int, string> nameByProcess)
    {
        if (WindowNative.GetWindowThreadProcessId(window, out var processIdValue) == 0)
        {
            return null;
        }

        var processId = (int)processIdValue;
        if (!nameByProcess.TryGetValue(processId, out var processName))
        {
            processName = ReadProcessName(processId);
            nameByProcess[processId] = processName;
        }

        if (!elevationByProcess.TryGetValue(processId, out var elevated))
        {
            elevated = IsElevated(processIdValue);
            elevationByProcess[processId] = elevated;
        }

        var style = WindowNative.GetWindowLong(window, WindowNative.GwlExStyle);
        var cloaked = WindowNative.DwmGetWindowAttribute(window, WindowNative.DwmaCloaked, out var cloakedValue, sizeof(int)) == 0 && cloakedValue != 0;
        return new WindowInfo(
            window,
            ReadText(window, title: true),
            ReadText(window, title: false),
            processId,
            processName,
            WindowNative.IsWindowVisible(window),
            cloaked,
            (style & WindowNative.WsExToolWindow) != 0,
            WindowNative.IsIconic(window),
            elevated);
    }

    private static string ReadText(nint window, bool title)
    {
        const int capacity = 512;
        var buffer = stackalloc char[capacity];
        var length = title ? WindowNative.GetWindowText(window, buffer, capacity) : WindowNative.GetClassName(window, buffer, capacity);
        return length <= 0 ? string.Empty : new string(buffer, 0, Math.Min(length, capacity - 1));
    }

    private static string ReadProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// True when the process runs elevated. A process whose token cannot be opened because access is denied is treated as
    /// elevated, since that is what Windows does to a non-elevated caller for an elevated or protected process.
    /// </summary>
    private static bool IsElevated(uint processId)
    {
        var process = WindowNative.OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return Marshal.GetLastPInvokeError() == WindowNative.ErrorAccessDenied;
        }

        try
        {
            if (!WindowNative.OpenProcessToken(process, TokenQuery, out var token))
            {
                return Marshal.GetLastPInvokeError() == WindowNative.ErrorAccessDenied;
            }

            try
            {
                return WindowNative.GetTokenInformation(token, TokenElevation, out var elevation, sizeof(int), out _) && elevation != 0;
            }
            finally
            {
                WindowNative.CloseHandle(token);
            }
        }
        finally
        {
            WindowNative.CloseHandle(process);
        }
    }
}
