using System.Diagnostics;
using System.Globalization;
using Presenta.Domain.Keyboard;
using Presenta.Keyboard.Windows;
using Presenta.Targeting;

namespace Presenta.Desktop.Tests;

/// <summary>A fact that only runs when <c>PRESENTA_DESKTOP_TESTS=1</c>, because it needs an interactive desktop and presses real keys.</summary>
public sealed class DesktopFactAttribute : FactAttribute
{
    public const string Variable = "PRESENTA_DESKTOP_TESTS";

    public DesktopFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
        {
            Skip = "Opt-in desktop test: set " + Variable + "=1 to run it (it opens real windows and presses real arrow keys).";
        }
    }
}

/// <summary>One probe process: a titled window that logs the arrow keys it receives.</summary>
internal sealed class Probe : IDisposable
{
    private readonly Process _process;
    private readonly string _directory;

    private Probe(Process process, string directory, long handle)
    {
        _process = process;
        _directory = directory;
        Handle = handle;
    }

    public long Handle { get; }

    public IReadOnlyList<string> Keys =>
        File.Exists(LogPath) ? File.ReadAllLines(LogPath).Where(l => l.Length > 0).ToArray() : Array.Empty<string>();

    private string LogPath => Path.Combine(_directory, "keys.log");

    public bool HasExited => _process.HasExited;

    public static Probe Start(string title)
    {
        var directory = Path.Combine(Path.GetTempPath(), "presenta-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var log = Path.Combine(directory, "keys.log");
        var ready = Path.Combine(directory, "ready.txt");
        var probeDll = Path.Combine(AppContext.BaseDirectory, "Presenta.WindowProbe.dll");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        start.ArgumentList.Add(probeDll);
        start.ArgumentList.Add("--title");
        start.ArgumentList.Add(title);
        start.ArgumentList.Add("--log");
        start.ArgumentList.Add(log);
        start.ArgumentList.Add("--ready");
        start.ArgumentList.Add(ready);
        var process = Process.Start(start) ?? throw new InvalidOperationException("The probe did not start.");
        WaitFor(() => File.Exists(ready) && new FileInfo(ready).Length > 0, TimeSpan.FromSeconds(15), "the probe window to open");
        return new Probe(process, directory, long.Parse(File.ReadAllText(ready), CultureInfo.InvariantCulture));
    }

    public void CloseWindow()
    {
        _process.CloseMainWindow();
        Assert.True(_process.WaitForExit(5000), "The probe window did not close.");
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        _process.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is harmless.
        }
    }

    public static void WaitFor(Func<bool> condition, TimeSpan timeout, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < timeout, "Timed out waiting for " + what + ".");
            using var pause = new ManualResetEventSlim(false);
            pause.Wait(25);
        }
    }
}

/// <summary>
/// The scenario that failed in manual testing: another program (Android Studio) is in the foreground while the watch asks
/// for the next slide. A "presentation" window and a "thief" window run in separate processes; the thief holds the
/// foreground; the real sender must deliver exactly one key to the presentation and none to the thief.
/// </summary>
[Trait("Category", "Desktop")]
public class RealWindowDeliveryTests
{
    [DesktopFact]
    public void With_another_window_in_the_foreground_the_key_reaches_only_the_chosen_window()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        using var presentation = Probe.Start("Presenta probe PRESENTATION " + token);
        using var thief = Probe.Start("Presenta probe THIEF " + token);
        var windows = new Win32WindowSystem();
        var service = new PresentationTargetService(windows, new InMemoryTargetSettingsStore());
        var candidate = service.ListCandidates().Single(c => c.Window.Title.EndsWith("PRESENTATION " + token, StringComparison.Ordinal));
        Assert.True(service.Select(candidate.Window));

        Probe.WaitFor(() => windows.GetForegroundWindow() == thief.Handle, TimeSpan.FromSeconds(5), "the thief window to hold the foreground");
        Assert.NotEqual(presentation.Handle, windows.GetForegroundWindow());

        var result = new WindowSlideKeyboard(service, windows).Send(SlideDirection.Next);

        Assert.Equal(KeyDeliveryResult.Delivered, result);
        Probe.WaitFor(() => presentation.Keys.Count >= 1, TimeSpan.FromSeconds(3), "the presentation window to receive the key");
        using var settle = new ManualResetEventSlim(false);
        settle.Wait(400);
        Assert.Equal(new[] { "Right" }, presentation.Keys);
        Assert.Empty(thief.Keys);
        Assert.Equal(presentation.Handle, windows.GetForegroundWindow()); // focus is deliberately not given back
    }

    /// <summary>Reproduces the reported defect: a bare SendInput goes to the foreground window, wherever the presentation is.</summary>
    [DesktopFact]
    public void Diagnosis_a_bare_key_press_goes_to_the_foreground_window_not_to_the_presentation()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        using var presentation = Probe.Start("Presenta probe PRESENTATION " + token);
        using var thief = Probe.Start("Presenta probe THIEF " + token);
        var windows = new Win32WindowSystem();
        Probe.WaitFor(() => windows.GetForegroundWindow() == thief.Handle, TimeSpan.FromSeconds(5), "the thief window to hold the foreground");

        Assert.True(windows.SendArrow(SlideDirection.Next));

        Probe.WaitFor(() => thief.Keys.Count >= 1, TimeSpan.FromSeconds(3), "the foreground window to receive the key");
        Assert.Equal(new[] { "Right" }, thief.Keys);
        Assert.Empty(presentation.Keys);
    }

    [DesktopFact]
    public void The_previous_key_is_the_left_arrow_and_goes_to_the_presentation()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        using var presentation = Probe.Start("Presenta probe PRESENTATION " + token);
        using var thief = Probe.Start("Presenta probe THIEF " + token);
        var windows = new Win32WindowSystem();
        var service = new PresentationTargetService(windows, new InMemoryTargetSettingsStore());
        service.Select(service.ListCandidates().Single(c => c.Window.Title.EndsWith("PRESENTATION " + token, StringComparison.Ordinal)).Window);
        Probe.WaitFor(() => windows.GetForegroundWindow() == thief.Handle, TimeSpan.FromSeconds(5), "the thief window to hold the foreground");

        Assert.Equal(KeyDeliveryResult.Delivered, new WindowSlideKeyboard(service, windows).Send(SlideDirection.Previous));

        Probe.WaitFor(() => presentation.Keys.Count >= 1, TimeSpan.FromSeconds(3), "the presentation window to receive the key");
        Assert.Equal(new[] { "Left" }, presentation.Keys);
        Assert.Empty(thief.Keys);
    }

    [DesktopFact]
    public void When_the_chosen_window_is_closed_nothing_is_pressed_anywhere()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        using var presentation = Probe.Start("Presenta probe PRESENTATION " + token);
        using var thief = Probe.Start("Presenta probe THIEF " + token);
        var windows = new Win32WindowSystem();
        var service = new PresentationTargetService(windows, new InMemoryTargetSettingsStore());
        service.Select(service.ListCandidates().Single(c => c.Window.Title.EndsWith("PRESENTATION " + token, StringComparison.Ordinal)).Window);
        presentation.CloseWindow();
        Probe.WaitFor(() => windows.GetForegroundWindow() == thief.Handle, TimeSpan.FromSeconds(5), "the thief window to hold the foreground");

        var result = new WindowSlideKeyboard(service, windows).Send(SlideDirection.Next);

        Assert.Equal(KeyDeliveryResult.TargetNotFound, result);
        using var settle = new ManualResetEventSlim(false);
        settle.Wait(400);
        Assert.Empty(thief.Keys);
        Assert.Empty(presentation.Keys);
    }

    [DesktopFact]
    public void A_minimized_presentation_is_restored_and_receives_the_key()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        using var presentation = Probe.Start("Presenta probe PRESENTATION " + token);
        var windows = new Win32WindowSystem();
        var service = new PresentationTargetService(windows, new InMemoryTargetSettingsStore());
        service.Select(service.ListCandidates().Single(c => c.Window.Title.EndsWith("PRESENTATION " + token, StringComparison.Ordinal)).Window);
        using var thief = Probe.Start("Presenta probe THIEF " + token);
        Assert.True(windows.GetWindow(presentation.Handle) is { IsMinimized: false });
        MinimizeWindow(presentation.Handle);
        Probe.WaitFor(() => windows.GetWindow(presentation.Handle)!.IsMinimized, TimeSpan.FromSeconds(3), "the presentation window to minimize");
        Probe.WaitFor(() => windows.GetForegroundWindow() == thief.Handle || windows.GetForegroundWindow() != presentation.Handle, TimeSpan.FromSeconds(3), "another window to be in front");

        Assert.Equal(TargetState.Minimized, service.GetState());
        var result = new WindowSlideKeyboard(service, windows).Send(SlideDirection.Next);

        Assert.Equal(KeyDeliveryResult.Delivered, result);
        Probe.WaitFor(() => presentation.Keys.Count >= 1, TimeSpan.FromSeconds(3), "the presentation window to receive the key");
        Assert.Equal(new[] { "Right" }, presentation.Keys);
        Assert.Empty(thief.Keys);
        Assert.False(windows.GetWindow(presentation.Handle)!.IsMinimized);
    }

    private static void MinimizeWindow(long handle) => Native.ShowWindow((nint)handle, 6);
}

internal static partial class Native
{
    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint window, int command);
}
