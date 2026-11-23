namespace Presenta.Companion.ViewModels;

/// <summary>Claims the right to be the only running companion of this Windows session.</summary>
public interface IInstanceLock : IDisposable
{
    /// <summary>True when this process is now the only instance (or already was). False when another instance holds it.</summary>
    bool TryAcquire();
}

/// <summary>
/// The real lock: a named event in the <c>Local\</c> namespace, which Windows scopes to the current user session (so two
/// different users on one PC do not block each other). The first process creates it and keeps it for its whole life; the
/// system removes it when that process exits, even after a crash. Nothing is ever signalled.
/// </summary>
public sealed class NamedEventInstanceLock : IInstanceLock
{
    public const string DefaultName = @"Local\Presenta.Companion.SingleInstance";

    private readonly string _name;
    private EventWaitHandle? _handle;

    public NamedEventInstanceLock(string name = DefaultName) => _name = name;

    public bool TryAcquire()
    {
        if (_handle is not null)
        {
            return true;
        }

        try
        {
            var handle = new EventWaitHandle(false, EventResetMode.ManualReset, _name, out var createdNew);
            if (createdNew)
            {
                _handle = handle;
                return true;
            }

            handle.Dispose();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // The name exists and belongs to a process we may not open (for example an instance running as administrator).
            return false;
        }
    }

    public void Dispose()
    {
        _handle?.Dispose();
        _handle = null;
    }
}

/// <summary>Decides at startup whether this process may continue (ADR-70). No window, no server: only the decision.</summary>
public static class SingleInstanceGate
{
    /// <summary>
    /// True when this is the only instance and startup may continue. Otherwise <paramref name="notify"/> receives the
    /// Spanish "already open" message (from the resource file) and false is returned: the caller must exit without creating a
    /// window, a server or any file.
    /// </summary>
    public static bool TryEnter(IInstanceLock instanceLock, Action<string> notify)
    {
        if (instanceLock.TryAcquire())
        {
            return true;
        }

        notify(UiText.Get("SingleInstance_Message"));
        return false;
    }
}
