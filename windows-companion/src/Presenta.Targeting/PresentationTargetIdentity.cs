namespace Presenta.Targeting;

/// <summary>
/// A stable description of the presentation window: which program, which window class, and part of the title.
/// It is what gets saved; a window handle is only a cache and dies with the window.
/// The title part is the title at selection time, normalized, so it can contain a file name.
/// </summary>
public sealed record PresentationTargetIdentity(string ProcessName, string WindowClass, string TitleMatch)
{
    public bool IsValid => ProcessName.Length > 0 && WindowClass.Length > 0;

    public static PresentationTargetIdentity From(WindowInfo window) =>
        new(window.ProcessName, window.ClassName, NormalizeTitle(window.Title));

    /// <summary>
    /// True when <paramref name="window"/> is the window this identity describes: same program (case-insensitive), same
    /// class, and a title that contains the stored part (case-insensitive, ignoring a leading "modified" marker).
    /// </summary>
    public bool Matches(WindowInfo window) =>
        string.Equals(ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(WindowClass, window.ClassName, StringComparison.Ordinal)
        && (TitleMatch.Length == 0
            || NormalizeTitle(window.Title).Contains(TitleMatch, StringComparison.OrdinalIgnoreCase));

    /// <summary>Trims spaces and a leading "unsaved changes" marker (such as an asterisk) that editors toggle while you work.</summary>
    internal static string NormalizeTitle(string title) => title.Trim().TrimStart('*', '•', '●', ' ');
}
