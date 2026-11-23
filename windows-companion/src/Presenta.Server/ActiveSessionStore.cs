using System.Text;
using System.Text.Json;
using Presenta.Domain;

namespace Presenta.Server;

/// <summary>
/// What is written to disk about the running or paused session, so that a crash does not lose it (ADR-67). The elapsed
/// time is the value at the last write: time after that write is not counted when the session is recovered.
/// </summary>
public sealed record ActiveSessionRecord(
    string Name,
    int PlannedSlides,
    long PlannedDurationMs,
    int CurrentSlide,
    long ElapsedMs,
    bool Paused,
    DateTimeOffset SavedAtUtc);

public enum ActiveSessionLoadStatus
{
    /// <summary>There is no file: the previous run ended normally.</summary>
    None,

    /// <summary>A readable, valid record.</summary>
    Valid,

    /// <summary>A file exists but is corrupt, partial or out of range. It is never used.</summary>
    Invalid,
}

public readonly record struct ActiveSessionLoad(ActiveSessionLoadStatus Status, ActiveSessionRecord? Record);

/// <summary>Storage of the active session. Implementations never throw: failures are reported by the return value.</summary>
public interface IActiveSessionStore
{
    /// <summary>Atomically replaces the stored record. False when it could not be written.</summary>
    bool Save(ActiveSessionRecord record);

    /// <summary>Reads the stored record, if any.</summary>
    ActiveSessionLoad Load();

    /// <summary>Removes the stored record. True when nothing is stored any more.</summary>
    bool Delete();
}

/// <summary>Stores nothing. The default, so a service created without a store behaves as before.</summary>
public sealed class NullActiveSessionStore : IActiveSessionStore
{
    public static readonly NullActiveSessionStore Instance = new();

    public bool Save(ActiveSessionRecord record) => true;

    public ActiveSessionLoad Load() => new(ActiveSessionLoadStatus.None, null);

    public bool Delete() => true;
}

/// <summary>Keeps the record in memory. For tests.</summary>
public class InMemoryActiveSessionStore : IActiveSessionStore
{
    public ActiveSessionRecord? Record { get; set; }

    public bool Corrupt { get; set; }

    public int Saves { get; private set; }

    public bool Save(ActiveSessionRecord record)
    {
        Record = record;
        Corrupt = false;
        Saves++;
        return true;
    }

    public ActiveSessionLoad Load() =>
        Corrupt ? new(ActiveSessionLoadStatus.Invalid, null)
        : Record is null ? new(ActiveSessionLoadStatus.None, null)
        : new(ActiveSessionLoadStatus.Valid, Record);

    public virtual bool Delete()
    {
        Record = null;
        Corrupt = false;
        return true;
    }
}

/// <summary>
/// A small JSON file, by default <c>%LOCALAPPDATA%\Presenta\active-session.json</c>. Writes go to a temporary file that
/// then replaces the real one, so a crash leaves either the old or the new content. It never contains the pairing code.
/// </summary>
public sealed class JsonActiveSessionStore : IActiveSessionStore
{
    private const int FileVersion = 1;
    private const long MaxElapsedMs = 7L * 24 * 3600 * 1000;

    private readonly string _path;

    public JsonActiveSessionStore(string path) => _path = path;

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Presenta", "active-session.json");

    public bool Save(ActiveSessionRecord record)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(new
            {
                version = FileVersion,
                name = record.Name,
                plannedSlides = record.PlannedSlides,
                plannedDurationMs = record.PlannedDurationMs,
                currentSlide = record.CurrentSlide,
                elapsedMs = record.ElapsedMs,
                state = record.Paused ? "paused" : "running",
                savedAtUnixMs = record.SavedAtUtc.ToUnixTimeMilliseconds(),
            });
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    public ActiveSessionLoad Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new(ActiveSessionLoadStatus.None, null);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.GetProperty("version").GetInt32() != FileVersion
                || root.GetProperty("name").ValueKind != JsonValueKind.String)
            {
                return Invalid();
            }

            var name = root.GetProperty("name").GetString() ?? string.Empty;
            var plannedSlides = root.GetProperty("plannedSlides").GetInt32();
            var plannedDurationMs = root.GetProperty("plannedDurationMs").GetInt64();
            var currentSlide = root.GetProperty("currentSlide").GetInt32();
            var elapsedMs = root.GetProperty("elapsedMs").GetInt64();
            var state = root.GetProperty("state").GetString();
            var savedAt = DateTimeOffset.FromUnixTimeMilliseconds(root.GetProperty("savedAtUnixMs").GetInt64());

            var valid = !string.IsNullOrWhiteSpace(name)
                && name.Length <= SessionLimits.MaxNameLength
                && plannedSlides is >= SessionLimits.MinSlides and <= SessionLimits.MaxSlides
                && plannedDurationMs is >= SessionLimits.MinPlannedDurationMs and <= SessionLimits.MaxPlannedDurationMs
                && currentSlide >= 1 && currentSlide <= plannedSlides
                && elapsedMs is >= 0 and <= MaxElapsedMs
                && state is "running" or "paused";
            return valid
                ? new(ActiveSessionLoadStatus.Valid, new ActiveSessionRecord(name, plannedSlides, plannedDurationMs, currentSlide, elapsedMs, state == "paused", savedAt))
                : Invalid();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException
            or FormatException or ArgumentOutOfRangeException or DecoderFallbackException or KeyNotFoundException or NotSupportedException)
        {
            return Invalid();
        }
    }

    public bool Delete()
    {
        try
        {
            File.Delete(_path);
            File.Delete(_path + ".tmp");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ActiveSessionLoad Invalid() => new(ActiveSessionLoadStatus.Invalid, null);
}

/// <summary>Turns an orphaned active-session record into the summary saved to the history.</summary>
public static class ActiveSessionRecovery
{
    /// <summary>
    /// The summary of a session that was cut short: interrupted by disconnection, lasting the elapsed time of the last
    /// write and finishing at the time of that write. The same record always gives the same summary, which is what lets
    /// the recovery detect a record that was already saved.
    /// </summary>
    public static SessionSummary BuildInterruptedSummary(ActiveSessionRecord record, SessionOptions? options = null) =>
        new(
            record.Name,
            record.SavedAtUtc,
            record.PlannedDurationMs,
            record.ElapsedMs,
            record.PlannedSlides,
            record.CurrentSlide,
            PaceCalculator.AverageSlidesPerMinute(options ?? new SessionOptions(), record.CurrentSlide, TimeSpan.FromMilliseconds(record.ElapsedMs)),
            FinalState.InterruptedByDisconnection,
            record.ElapsedMs <= record.PlannedDurationMs);
}
