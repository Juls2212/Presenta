using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Presenta.Ai;

/// <summary>
/// Turns whatever the model answered into a single short line, or into nothing (ADR-77). Nothing is displayed unless it
/// passes: a rejected text means the deterministic wording is used.
/// </summary>
public static partial class AiTextSanitizer
{
    private static readonly char[] SurroundingMarks =
        ['"', '\'', '`', '“', '”', '‘', '’', '«', '»', ' '];

    private static readonly char[] MarkdownMarks = ['*', '_', '`', '#', '~'];

    /// <summary>The cleaned text, or null when it must not be shown.</summary>
    public static string? Sanitize(string? raw, int maxLength = AiLimits.MaxTextLength)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsControl(c) && c is not ('\n' or '\r' or '\t'))
            {
                return null; // control characters are never acceptable
            }

            if (char.IsSurrogate(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.OtherSymbol or UnicodeCategory.Format)
            {
                return null; // emojis and invisible formatting characters
            }

            builder.Append(c);
        }

        var text = builder.ToString();
        if (Url().IsMatch(text))
        {
            return null;
        }

        text = Whitespace().Replace(text, " ").Trim();
        text = text.Trim(SurroundingMarks).Trim();
        foreach (var mark in MarkdownMarks)
        {
            text = text.Replace(mark.ToString(), string.Empty, StringComparison.Ordinal);
        }

        text = LeadingBullet().Replace(text, string.Empty);
        text = Whitespace().Replace(text, " ").Trim().Trim(SurroundingMarks).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length > maxLength)
        {
            text = TruncateAtWord(text, maxLength);
            if (text.Length == 0)
            {
                return null;
            }
        }

        return text;
    }

    // One hard cut at a word boundary, kept only when a few words remain; otherwise empty (the text is rejected).
    private static string TruncateAtWord(string text, int maxLength)
    {
        var cut = text[..maxLength];
        if (!char.IsWhiteSpace(text[maxLength]))
        {
            var lastSpace = cut.LastIndexOf(' ');
            if (lastSpace <= 0)
            {
                return string.Empty;
            }

            cut = cut[..lastSpace];
        }

        cut = cut.TrimEnd(' ', ',', ';', ':', '-', '.');
        var words = cut.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return words >= AiLimits.MinWordsAfterTruncation ? cut : string.Empty;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^\s*(?:[-+>]|\d+[.)])\s+")]
    private static partial Regex LeadingBullet();

    [GeneratedRegex(@"(?i)(?:[a-z][a-z0-9+.-]*://|\bwww\.|\b[a-z0-9-]+\.(?:com|net|org|io|co|app|dev|ai|me|info|xyz)\b)")]
    private static partial Regex Url();
}
