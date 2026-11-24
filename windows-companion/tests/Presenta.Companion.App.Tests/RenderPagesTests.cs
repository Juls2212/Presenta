using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Presenta.Companion.ViewModels;
using Presenta.TestSupport;

namespace Presenta.Companion.App.Tests;

/// <summary>A fact that only runs when <c>PRESENTA_DESKTOP_TESTS=1</c>. This one only renders pictures; it never shows a window.</summary>
public sealed class PreviewFactAttribute : FactAttribute
{
    public const string Variable = "PRESENTA_DESKTOP_TESTS";

    public PreviewFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
        {
            Skip = "Opt-in: set " + Variable + "=1 to write the page pictures to docs/ui-previews/desktop/.";
        }
    }
}

/// <summary>
/// Renders each page of the shell to a PNG, offscreen (a laid-out control drawn into a bitmap; no <see cref="Window"/> exists), plus
/// one at 150% scaling and one at the minimum size. The pictures show the window with fake data and no session.
/// </summary>
public class RenderPagesTests
{
    private static string OutputFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "docs", "ui-previews")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var folder = Path.Combine(directory!.FullName, "docs", "ui-previews", "desktop");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Save(FrameworkElement element, double width, double height, double scale, string path)
    {
        Sta.Realize(element, width, height);
        var bitmap = new RenderTargetBitmap((int)Math.Round(width * scale), (int)Math.Round(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        // The window background is drawn by the window, which does not exist here: paint it first from the same token.
        var background = new System.Windows.Shapes.Rectangle
        {
            Width = width,
            Height = height,
            Fill = (Brush)Application.Current.Resources["WindowBackgroundBrush"],
        };
        background.Measure(new Size(width, height));
        background.Arrange(new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    [PreviewFact]
    public void Each_page_is_rendered_to_a_picture()
    {
        var folder = OutputFolder();
        using var rig = Rig.Create(
            true,
            true,
            FakeWindowSystem.Window(10, "Slide Show - Quarterly review.pptx", "POWERPNT", "screenClass"),
            FakeWindowSystem.Window(11, "Quarterly review.pptx - PowerPoint", "POWERPNT", "PPTFrameClass"),
            FakeWindowSystem.Window(12, "notes.txt - Notepad", "notepad", "Notepad"),
            FakeWindowSystem.Window(13, "News - Browser", "chrome", "Chrome_WidgetWin_1"),
            FakeWindowSystem.Window(14, "Messages", "chat", "ChatWindow"));
        Sta.Run(() =>
        {
            var shell = rig.CreateShell();
            foreach (var section in Enum.GetValues<Section>())
            {
                rig.ViewModel.Navigation.Select(section);
                Save(shell, 1040, 680, 1.0, Path.Combine(folder, $"{(int)section + 1:00}-{section.ToString().ToLowerInvariant()}.png"));
            }

            rig.ViewModel.Navigation.Select(Section.Home);
            Save(shell, 1040, 680, 1.5, Path.Combine(folder, "07-home-150-percent.png"));
            rig.ViewModel.Navigation.Select(Section.Window);
            Save(shell, 900, 600, 1.0, Path.Combine(folder, "08-window-minimum-size.png"));
        });
    }
}
