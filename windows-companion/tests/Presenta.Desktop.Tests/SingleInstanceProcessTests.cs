using System.Diagnostics;
using Presenta.Companion.ViewModels;

namespace Presenta.Desktop.Tests;

/// <summary>
/// The real named object across two processes (ADR-70). A helper process that this test creates holds a named event; the
/// real <see cref="NamedEventInstanceLock"/> in this process must see it as taken, and as free again once the helper is
/// gone, including when the helper is killed (a crash). No window is opened and no key is pressed.
/// </summary>
[Trait("Category", "Desktop")]
public class SingleInstanceProcessTests
{
    private static Process StartHolder(string name, string readyFile)
    {
        var probeDll = Path.Combine(AppContext.BaseDirectory, "Presenta.WindowProbe.dll");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true };
        start.ArgumentList.Add(probeDll);
        start.ArgumentList.Add("--hold-event");
        start.ArgumentList.Add(name);
        start.ArgumentList.Add("--ready");
        start.ArgumentList.Add(readyFile);
        var process = Process.Start(start) ?? throw new InvalidOperationException("The helper did not start.");
        Probe.WaitFor(() => File.Exists(readyFile) && new FileInfo(readyFile).Length > 0, TimeSpan.FromSeconds(15), "the helper to hold the name");
        return process;
    }

    [DesktopFact]
    public void A_name_held_by_another_process_is_taken_and_is_free_again_after_that_process_exits()
    {
        var name = @"Local\Presenta.Tests." + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "presenta-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var ready = Path.Combine(directory, "ready.txt");
        Process? holder = null;
        try
        {
            holder = StartHolder(name, ready);
            Assert.Equal("created", File.ReadAllText(ready));

            using var second = new NamedEventInstanceLock(name);
            Assert.False(second.TryAcquire());

            holder.StandardInput.WriteLine();
            Assert.True(holder.WaitForExit(10_000), "The helper did not exit.");

            using var third = new NamedEventInstanceLock(name);
            Assert.True(third.TryAcquire());
        }
        finally
        {
            Cleanup(holder, directory);
        }
    }

    [DesktopFact]
    public void A_name_held_by_a_process_that_is_killed_is_released_by_the_system()
    {
        var name = @"Local\Presenta.Tests." + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "presenta-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var ready = Path.Combine(directory, "ready.txt");
        Process? holder = null;
        try
        {
            holder = StartHolder(name, ready);
            using var blocked = new NamedEventInstanceLock(name);
            Assert.False(blocked.TryAcquire());

            holder.Kill();
            Assert.True(holder.WaitForExit(10_000), "The helper did not die.");

            using var free = new NamedEventInstanceLock(name);
            Assert.True(free.TryAcquire());
        }
        finally
        {
            Cleanup(holder, directory);
        }
    }

    [DesktopFact]
    public void The_first_owner_in_this_process_blocks_a_helper_that_asks_for_the_same_name()
    {
        var name = @"Local\Presenta.Tests." + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "presenta-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var ready = Path.Combine(directory, "ready.txt");
        Process? helper = null;
        try
        {
            using var mine = new NamedEventInstanceLock(name);
            Assert.True(mine.TryAcquire());

            helper = StartHolder(name, ready);

            Assert.Equal("existing", File.ReadAllText(ready)); // the helper found the name already taken
        }
        finally
        {
            Cleanup(helper, directory);
        }
    }

    private static void Cleanup(Process? process, string directory)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        process?.Dispose();
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is harmless.
        }
    }
}
