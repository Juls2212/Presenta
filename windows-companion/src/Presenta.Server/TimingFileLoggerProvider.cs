#if DEBUG
using Microsoft.Extensions.Logging;

namespace Presenta.Server;

/// <summary>
/// Debug builds only. Appends every log line that contains <c>PresentaTiming</c> or <c>PresentaDebug</c> to a text file, so
/// the latency of each key delivery (RNF 03), its typed result and the Win32 error codes of each activation attempt can be
/// read after a manual run. The pairing code is never logged. Release builds do not contain this class at all.
/// </summary>
public sealed class TimingFileLoggerProvider : ILoggerProvider
{
    public const string Marker = "PresentaTiming";
    public const string DebugMarker = "PresentaDebug";

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    public TimingFileLoggerProvider(string path, TimeProvider? time = null)
    {
        _path = path;
        _time = time ?? TimeProvider.System;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this);

    public void Dispose()
    {
    }

    private void Append(string message)
    {
        lock (_gate)
        {
            try
            {
                File.AppendAllText(_path, _time.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
            catch (IOException)
            {
                // A diagnostics file must never disturb the app.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly TimingFileLoggerProvider _owner;

        public FileLogger(TimingFileLoggerProvider owner) => _owner = owner;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            if (text.Contains(Marker, StringComparison.Ordinal) || text.Contains(DebugMarker, StringComparison.Ordinal))
            {
                _owner.Append(text);
            }
        }
    }
}
#endif
