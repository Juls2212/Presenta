using System.Globalization;

namespace Presenta.Companion.ViewModels;

/// <summary>
/// Optional command-line settings of the desktop app: <c>--port 8765</c>, <c>--database C:\path\history.db</c> and
/// <c>--target-settings C:\path\target.json</c>.
/// Anything else is ignored.
/// </summary>
public sealed record LaunchArguments(int? Port, string? DatabasePath, string? TargetSettingsPath = null)
{
    public static LaunchArguments Parse(IReadOnlyList<string> arguments)
    {
        int? port = null;
        string? database = null;
        string? targetSettings = null;
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
        }

        return new LaunchArguments(port, database, targetSettings);
    }
}
