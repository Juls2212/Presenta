using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Presenta.Companion.App.Tests;

/// <summary>
/// Reads Themes/Tokens.xaml as plain XML (no WPF needed) and checks the text and graphic pairs the window really uses, composited
/// over the layers they sit on. Also guards that no color value appears outside the token file.
/// </summary>
public class DesignTokenTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string AppFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Presenta.Companion.App")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "Presenta.Companion.App");
    }

    private readonly record struct Rgba(double R, double G, double B, double A);

    private static Dictionary<string, Rgba> Colors()
    {
        var document = XDocument.Load(Path.Combine(AppFolder(), "Themes", "Tokens.xaml"));
        return document.Descendants()
            .Where(e => e.Name.LocalName == "Color")
            .ToDictionary(e => (string)e.Attribute(Xaml + "Key")!, e => Parse(e.Value.Trim()));
    }

    private static Rgba Parse(string hex)
    {
        var digits = hex.TrimStart('#');
        var alpha = digits.Length == 8 ? int.Parse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0 : 1.0;
        var rgb = digits[^6..];
        return new Rgba(
            int.Parse(rgb[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(rgb[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(rgb[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            alpha);
    }

    private static Rgba Over(Rgba top, Rgba bottom) => new(
        top.R * top.A + bottom.R * (1 - top.A),
        top.G * top.A + bottom.G * (1 - top.A),
        top.B * top.A + bottom.B * (1 - top.A),
        1);

    private static double Luminance(Rgba c)
    {
        static double Channel(double v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Ratio(Rgba foreground, Rgba background)
    {
        var a = Luminance(foreground);
        var b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>The surfaces text can sit on: each graphite end, with each translucent layer over it (and a few stacks).</summary>
    private static IEnumerable<(string Name, Rgba Color)> Surfaces(Dictionary<string, Rgba> c, params string[][] stacks)
    {
        foreach (var baseName in new[] { "Graphite1", "Graphite2" })
        {
            foreach (var stack in stacks)
            {
                var color = c[baseName];
                foreach (var layer in stack)
                {
                    color = Over(c[layer], color);
                }

                yield return ($"{string.Join("+", stack.Prepend(baseName))}", color);
            }
        }
    }

    public static IEnumerable<object[]> TextPairs()
    {
        var c = Colors();
        string[][] common =
        {
            Array.Empty<string>(),
            new[] { "Glass" },
            new[] { "Glass", "GlassStrong" },
            new[] { "Glass", "GlassHover" },
            new[] { "Glass", "Scrim" },
        };

        foreach (var (name, background) in Surfaces(c, common.Append(new[] { "Glass", "CyanSoft" }).ToArray()))
        {
            yield return new object[] { "Ink", name, Ratio(c["Ink"], background), 4.5 };
        }

        // Muted text sits on the page, on the panel, on a hovered row of the panel (two glass layers) and on the dark quote box.
        string[][] mutedSurfaces =
        {
            Array.Empty<string>(),
            new[] { "Glass" },
            new[] { "Glass", "Glass" },
            new[] { "Glass", "Scrim" },
        };
        foreach (var (name, background) in Surfaces(c, mutedSurfaces))
        {
            yield return new object[] { "Muted", name, Ratio(c["Muted"], background), 4.5 };
        }

        // Dim is used for the small headings and the dim marks, on the plain page and on the panel.
        foreach (var (name, background) in Surfaces(c, new[] { Array.Empty<string>(), new[] { "Glass" } }))
        {
            yield return new object[] { "Dim", name, Ratio(c["Dim"], background), 4.5 };
        }

        foreach (var accent in new[] { "Cyan", "Amber", "Orange", "Red" })
        {
            var stacks = new List<string[]> { Array.Empty<string>(), new[] { "Glass" }, new[] { "Glass", "Scrim" } };
            if (accent == "Cyan")
            {
                stacks.Add(new[] { "Glass", "CyanSoft" });
            }

            if (accent == "Amber")
            {
                stacks.Add(new[] { "Glass", "AmberSoft" });
            }

            foreach (var (name, background) in Surfaces(c, stacks.ToArray()))
            {
                yield return new object[] { accent, name, Ratio(c[accent], background), 4.5 };
            }
        }

        // Text on top of an accent fill.
        yield return new object[] { "OnCyan", "Cyan", Ratio(c["OnCyan"], c["Cyan"]), 4.5 };
        yield return new object[] { "OnOk", "Ok", Ratio(c["OnOk"], c["Ok"]), 4.5 };
        yield return new object[] { "OnAmber", "Amber", Ratio(c["OnAmber"], c["Amber"]), 4.5 };
        yield return new object[] { "OnRed", "Red", Ratio(c["OnRed"], c["Red"]), 4.5 };

        // Graphics (marks, ring, focus ring, borders that carry meaning) need 3:1.
        foreach (var graphic in new[] { "Cyan", "Amber", "Red", "Ok", "Dim", "Orange" })
        {
            foreach (var (name, background) in Surfaces(c, new[] { Array.Empty<string>(), new[] { "Glass" } }))
            {
                yield return new object[] { graphic + " (graphic)", name, Ratio(c[graphic], background), 3.0 };
            }
        }
    }

    [Theory]
    [MemberData(nameof(TextPairs))]
    public void Every_token_pair_reaches_its_contrast(string foreground, string background, double ratio, double required) =>
        Assert.True(ratio >= required, $"{foreground} on {background} is {ratio:0.00}:1, below {required}:1");

    [Fact]
    public void The_accent_colors_differ_from_each_other()
    {
        var c = Colors();
        Assert.True(Ratio(c["Red"], c["Amber"]) > 1.2);
        Assert.True(Ratio(c["Orange"], c["Amber"]) > 1.0);
        Assert.NotEqual(c["Cyan"], c["Amber"]);
    }

    [Fact]
    public void The_palette_has_the_values_of_the_approved_mockup()
    {
        var c = Colors();
        Assert.Equal(Parse("#14161B"), c["Graphite1"]);
        Assert.Equal(Parse("#1C2028"), c["Graphite2"]);
        Assert.Equal(Parse("#ECEFF4"), c["Ink"]);
        Assert.Equal(Parse("#A3ABBA"), c["Muted"]);
        Assert.Equal(Parse("#22D3EE"), c["Cyan"]);
        Assert.Equal(Parse("#FFB020"), c["Amber"]);
        Assert.Equal(Parse("#FF8A3D"), c["Orange"]);
        Assert.Equal(Parse("#FF5C5C"), c["Red"]);
        Assert.Equal(Parse("#5BE3A0"), c["Ok"]);
    }

    [Fact]
    public void No_color_value_appears_outside_the_token_file()
    {
        var hex = new Regex(@"#[0-9A-Fa-f]{3,8}\b", RegexOptions.CultureInvariant);
        var named = new Regex(@"(Foreground|Background|Fill|Stroke|BorderBrush|Color)\s*=\s*""(?!\{)[^""]*""", RegexOptions.CultureInvariant);
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(AppFolder(), "*.xaml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(AppFolder(), file);
            if (relative.StartsWith("obj", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("bin", StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith("Tokens.xaml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match match in hex.Matches(text))
            {
                offenders.Add($"{relative}: {match.Value}");
            }

            foreach (Match match in named.Matches(text))
            {
                offenders.Add($"{relative}: {match.Value}");
            }
        }

        // Code-behind and converters must not build colors either.
        foreach (var file in Directory.EnumerateFiles(AppFolder(), "*.cs", SearchOption.TopDirectoryOnly).Concat(Directory.EnumerateFiles(Path.Combine(AppFolder(), "Views"), "*.cs")))
        {
            var text = File.ReadAllText(file);
            if (Regex.IsMatch(text, @"Color\.From|new SolidColorBrush|\(Color\)ColorConverter|(?<!System)Colors\.[A-Z]"))
            {
                offenders.Add($"{Path.GetRelativePath(AppFolder(), file)}: builds a color in code");
            }
        }

        Assert.True(offenders.Count == 0, "Colors belong in Themes/Tokens.xaml only:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void No_user_visible_text_is_written_in_xaml_or_in_the_app_code()
    {
        var literal = new Regex(@"(Text|Content|Header|ToolTip|Title|AutomationProperties\.Name|AutomationProperties\.HelpText)\s*=\s*""(?!\{)[^""]*[A-Za-z][^""]*""", RegexOptions.CultureInvariant);
        var accents = new Regex("[" + new string(new[] { (char)0xE1, (char)0xE9, (char)0xED, (char)0xF3, (char)0xFA, (char)0xF1, (char)0xBF, (char)0xA1, (char)0xC1, (char)0xC9, (char)0xCD, (char)0xD3, (char)0xDA, (char)0xD1 }) + "]");
        var offenders = new List<string>();
        var files = Directory.EnumerateFiles(AppFolder(), "*.xaml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(AppFolder(), "*.cs", SearchOption.AllDirectories));
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(AppFolder(), file);
            if (relative.StartsWith("obj", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("bin", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match match in literal.Matches(text))
            {
                offenders.Add($"{relative}: {match.Value}");
            }

            if (accents.IsMatch(text))
            {
                offenders.Add($"{relative}: accented characters");
            }
        }

        Assert.True(offenders.Count == 0, "User-visible text belongs in UiStrings.resx: " + string.Join("; ", offenders));
    }

    [Fact]
    public void Every_brush_the_pages_use_exists_as_a_token()
    {
        var tokens = File.ReadAllText(Path.Combine(AppFolder(), "Themes", "Tokens.xaml"));
        var defined = Regex.Matches(tokens, @"x:Key=""(\w+)""").Select(m => m.Groups[1].Value).ToHashSet();
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(AppFolder(), "*.xaml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(AppFolder(), file);
            if (relative.StartsWith("obj", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("bin", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\{(?:Dynamic|Static)Resource (\w+Brush)\}"))
            {
                used.Add(match.Groups[1].Value);
            }
        }

        var controls = File.ReadAllText(Path.Combine(AppFolder(), "Themes", "Controls.xaml"));
        var all = defined.Concat(Regex.Matches(controls, @"x:Key=""(\w+)""").Select(m => m.Groups[1].Value)).ToHashSet();
        Assert.All(used, name => Assert.Contains(name, all));
    }
}
