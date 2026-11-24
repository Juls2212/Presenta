using System.Windows;
using System.Windows.Media;

namespace Presenta.Companion.App;

/// <summary>
/// When Windows high contrast is on, every design token that carries a color is replaced by a system color, so the window follows
/// the user's theme. Statuses keep their shapes and words, so nothing is lost when the accent colors collapse. Turning high contrast
/// off removes the replacements and the original tokens show again.
/// </summary>
public static class HighContrastTokens
{
    private static readonly string[] Replaced =
    {
        "InkBrush", "MutedBrush", "DimBrush", "CyanBrush", "AmberBrush", "OrangeBrush", "RedBrush", "OkBrush",
        "OnCyanBrush", "OnOkBrush", "OnAmberBrush", "OnRedBrush", "GlassBrush", "GlassStrongBrush", "GlassHoverBrush",
        "GlassLineBrush", "CyanSoftBrush", "AmberSoftBrush", "ScrimBrush", "RingInnerBrush", "RingTrackBrush", "RailBrush",
        "WindowBackgroundBrush", "GlassBorderBrush",
    };

    /// <summary>Applies or removes the replacements according to <see cref="SystemParameters.HighContrast"/>.</summary>
    public static void Apply(ResourceDictionary resources)
    {
        if (!SystemParameters.HighContrast)
        {
            foreach (var key in Replaced)
            {
                resources.Remove(key);
            }

            return;
        }

        foreach (var key in Replaced)
        {
            resources[key] = Map(key);
        }
    }

    private static Brush Map(string key) => key switch
    {
        "InkBrush" or "MutedBrush" or "AmberBrush" or "OrangeBrush" or "RedBrush" or "OkBrush" => SystemColors.WindowTextBrush,
        "DimBrush" or "RingTrackBrush" => SystemColors.GrayTextBrush,
        "CyanBrush" => SystemColors.HotTrackBrush,
        "OnCyanBrush" => SystemColors.WindowBrush,
        "OnOkBrush" or "OnAmberBrush" or "OnRedBrush" => SystemColors.WindowBrush,
        "GlassStrongBrush" or "ScrimBrush" or "RingInnerBrush" or "WindowBackgroundBrush" => SystemColors.WindowBrush,
        "GlassLineBrush" or "GlassBorderBrush" => SystemColors.WindowTextBrush,
        "CyanSoftBrush" => SystemColors.WindowBrush,
        _ => Brushes.Transparent,
    };
}
