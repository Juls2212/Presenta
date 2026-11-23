using System.Globalization;

namespace Presenta.Companion.ViewModels;

/// <summary>
/// Optional command-line settings of the desktop app: <c>--port 8765</c>, <c>--database C:\path\history.db</c>,
/// <c>--target-settings C:\path\target.json</c>, <c>--settings C:\path\settings.json</c> and
/// <c>--active-session C:\path\active-session.json</c>. Anything else is ignored.
/// </summary>
public sealed record LaunchArguments(
    int? Port,
    string? DatabasePath,
    string? TargetSettingsPath = null,
    string? SettingsPath = null,
    string? ActiveSessionPath = null)
{
    public static LaunchArguments Parse(IReadOnlyList<string> arguments)
    {
        int? port = null;
        string? database = null;
        string? targetSettings = null;
        string? settings = null;
        string? activeSession = null;
        for (var i = 0; i < arguments.Count - 1; i++)
        {
            if (arguments[i] == "--port"
                && int.TryParse(arguments[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && parsed is >= 0 and <= 65535)
            {
                port = parsed;
                i++;
            }
            else if (arguments[i] == "--database" && !string.IsNullOrWhiteSpace(arguments[i + 1]))
            {
                database = arguments[i + 1];
                i++;
            }
            else if (arguments[i] == "--target-settings" && !string.IsNullOrWhiteSpace(arguments[i + 1]))
            {
                targetSettings = arguments[i + 1];
                i++;
            }
            else if (arguments[i] == "--settings" && !string.IsNullOrWhiteSpace(arguments[i + 1]))
            {
                settings = arguments[i + 1];
                i++;
            }
            else if (arguments[i] == "--active-session" && !string.IsNullOrWhiteSpace(arguments[i + 1]))
            {
                activeSession = arguments[i + 1];
                i++;
            }
        }

        return new LaunchArguments(port, database, targetSettings, settings, activeSession);
    }
}
