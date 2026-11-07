using System.Text;
using System.Text.Json;

namespace Presenta.Targeting;

/// <summary>Persistence of the chosen presentation window. Loading never throws.</summary>
public interface ITargetSettingsStore
{
    /// <summary>The saved identity, or null when nothing is saved or the file is missing, corrupt or invalid.</summary>
    PresentationTargetIdentity? Load();

    /// <summary>Saves the identity (null clears it). Returns false when the file could not be written.</summary>
    bool Save(PresentationTargetIdentity? identity);
}

/// <summary>Keeps the identity in memory. For tests and for runs that must not touch the disk.</summary>
public sealed class InMemoryTargetSettingsStore : ITargetSettingsStore
{
    public InMemoryTargetSettingsStore(PresentationTargetIdentity? initial = null) => Identity = initial;

    public PresentationTargetIdentity? Identity { get; private set; }

    public PresentationTargetIdentity? Load() => Identity;

    public bool Save(PresentationTargetIdentity? identity)
    {
        Identity = identity;
        return true;
    }
}

/// <summary>
/// A small JSON file, by default <c>%LOCALAPPDATA%\Presenta\target.json</c>. It holds only the program name, the window
/// class and the title part; nothing secret (never the pairing code).
/// </summary>
public sealed class JsonTargetSettingsStore : ITargetSettingsStore
{
    private const int FileVersion = 1;

    private readonly string _path;

    public JsonTargetSettingsStore(string path) => _path = path;

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Presenta", "target.json");

    public PresentationTargetIdentity? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("target", out var target)
                || target.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var identity = new PresentationTargetIdentity(
                ReadString(target, "processName"),
                ReadString(target, "windowClass"),
                ReadString(target, "titleMatch"));
            return identity.IsValid ? identity : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or DecoderFallbackException)
        {
            return null;
        }
    }

    public bool Save(PresentationTargetIdentity? identity)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(
                new
                {
                    version = FileVersion,
                    target = identity is null
                        ? null
                        : new { processName = identity.ProcessName, windowClass = identity.WindowClass, titleMatch = identity.TitleMatch },
                },
                new JsonSerializerOptions { WriteIndented = true });
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
