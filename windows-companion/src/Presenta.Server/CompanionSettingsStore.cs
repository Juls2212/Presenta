using System.Text;
using System.Text.Json;

namespace Presenta.Server;

/// <summary>
/// Settings of the companion that survive a restart. The pairing code is stored only when the user turned on
/// "remember the pairing code" (ADR-69); otherwise <see cref="PairingCode"/> is null.
/// </summary>
public sealed record CompanionSettings(bool RememberPairingCode, string? PairingCode)
{
    public static readonly CompanionSettings Default = new(false, null);

    /// <summary>A stored code is usable only when it has exactly six digits.</summary>
    public static bool IsValidCode(string? code) =>
        code is { Length: 6 } && code.All(c => c is >= '0' and <= '9');
}

/// <summary>Storage of <see cref="CompanionSettings"/>. Implementations never throw.</summary>
public interface ICompanionSettingsStore
{
    /// <summary>The saved settings, or <see cref="CompanionSettings.Default"/> when the file is missing, corrupt or invalid.</summary>
    CompanionSettings Load();

    /// <summary>Saves the settings. False when they could not be written.</summary>
    bool Save(CompanionSettings settings);
}

/// <summary>Stores nothing. The default for a service created without a store.</summary>
public sealed class NullCompanionSettingsStore : ICompanionSettingsStore
{
    public static readonly NullCompanionSettingsStore Instance = new();

    public CompanionSettings Load() => CompanionSettings.Default;

    public bool Save(CompanionSettings settings) => true;
}

/// <summary>Keeps the settings in memory. For tests.</summary>
public sealed class InMemoryCompanionSettingsStore : ICompanionSettingsStore
{
    public InMemoryCompanionSettingsStore(CompanionSettings? initial = null) => Current = initial ?? CompanionSettings.Default;

    public CompanionSettings Current { get; private set; }

    public CompanionSettings Load() => Current;

    public bool Save(CompanionSettings settings)
    {
        Current = settings;
        return true;
    }
}

/// <summary>
/// A small JSON file, by default <c>%LOCALAPPDATA%\Presenta\settings.json</c>. The code is stored in plain text in the
/// user's own profile folder, the same trust level as the rest of the user's local files (ADR-69).
/// </summary>
public sealed class JsonCompanionSettingsStore : ICompanionSettingsStore
{
    private const int FileVersion = 1;

    private readonly string _path;

    public JsonCompanionSettingsStore(string path) => _path = path;

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Presenta", "settings.json");

    public CompanionSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return CompanionSettings.Default;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("rememberPairingCode", out var remember)
                || remember.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return CompanionSettings.Default;
            }

            if (!remember.GetBoolean())
            {
                return CompanionSettings.Default;
            }

            var code = root.TryGetProperty("pairingCode", out var stored) && stored.ValueKind == JsonValueKind.String ? stored.GetString() : null;
            return new CompanionSettings(true, CompanionSettings.IsValidCode(code) ? code : null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or DecoderFallbackException)
        {
            return CompanionSettings.Default;
        }
    }

    public bool Save(CompanionSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(
                new
                {
                    version = FileVersion,
                    rememberPairingCode = settings.RememberPairingCode,
                    pairingCode = settings.RememberPairingCode ? settings.PairingCode : null,
                },
                new JsonSerializerOptions { WriteIndented = true });
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
}
