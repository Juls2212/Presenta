using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using Presenta.Companion.App.Views;
using Presenta.Companion.ViewModels;
using Presenta.Targeting;
using Presenta.TestSupport;

namespace Presenta.Companion.App.Tests;

/// <summary>
/// Loads the real shell with fake services, never shown, and visits the six pages. Each page must have its key controls, every
/// interactive element must have a spoken name and be at least 32 px in both directions.
/// </summary>
public class UiSmokeTests
{
    private static readonly WindowInfo Show = FakeWindowSystem.Window(10, "Slide Show - deck.pptx", "POWERPNT", "screenClass");
    private static readonly WindowInfo Editing = FakeWindowSystem.Window(11, "deck.pptx - PowerPoint", "POWERPNT", "PPTFrameClass");
    private static readonly WindowInfo Notes = FakeWindowSystem.Window(12, "notes.txt - Notepad", "notepad", "Notepad");
    private static readonly WindowInfo Browser = FakeWindowSystem.Window(13, "News - Browser", "chrome", "Chrome_WidgetWin_1");

    private static Rig CreateRig() => Rig.Create(true, true, Show, Editing, Notes, Browser);

    private static void Visit(Rig rig, ShellView shell, Section section)
    {
        rig.ViewModel.Navigation.Select(section);
        Sta.Realize(shell);
    }

    private static string TextOf(Button button) => button.Content as string ?? string.Empty;

    private static bool IsActuallyShown(FrameworkElement element)
    {
        // Offscreen nothing is "visible" in the WPF sense, so walk the Visibility flags of the element and its parents.
        for (DependencyObject? current = element; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement ui && ui.Visibility != Visibility.Visible)
            {
                return false;
            }
        }

        return true;
    }

    [Fact]
    public void Every_page_loads_with_its_key_controls_and_every_control_is_named_and_big_enough()
    {
        using var rig = CreateRig();
        Sta.Run(() =>
        {
            var shell = rig.CreateShell();
            Sta.Realize(shell);

            foreach (var section in Enum.GetValues<Section>())
            {
                Visit(rig, shell, section);
                var page = Sta.All<ContentControl>(shell).First(c => c.Content is PageViewModel);
                Assert.Equal(section, ((PageViewModel)page.Content).Section);

                var shown = Sta.All<FrameworkElement>(page).Where(IsActuallyShown).ToList();
                Assert.NotEmpty(shown);
                foreach (var element in shown.Where(e => e is Button or CheckBox or ListBoxItem or ToggleButton or Expander))
                {
                    Assert.True(Sta.SpokenName(element).Length > 0, $"{section}: {element.GetType().Name} has no spoken name");
                    if (element is not Expander)
                    {
                        Assert.True(element.ActualHeight >= 32, $"{section}: {element.GetType().Name} '{Sta.SpokenName(element)}' is {element.ActualHeight} px tall");
                        Assert.True(element.ActualWidth >= 32, $"{section}: {element.GetType().Name} '{Sta.SpokenName(element)}' is {element.ActualWidth} px wide");
                    }
                }
            }
        });
    }

    [Fact]
    public void The_navigation_rail_has_six_named_buttons_and_the_settings_one_is_last()
    {
        using var rig = CreateRig();
        Sta.Run(() =>
        {
            var shell = rig.CreateShell();
            Sta.Realize(shell);

            var nav = Sta.All<Button>(shell).Where(b => b.DataContext is NavItemViewModel).ToList();
            Assert.Equal(6, nav.Count);
            Assert.All(nav, b => Assert.False(string.IsNullOrWhiteSpace(Sta.SpokenName(b))));
            Assert.Equal(Section.Settings, ((NavItemViewModel)nav[^1].DataContext).Section);
            Assert.All(nav, b => Assert.True(b.ActualHeight >= 32));
        });
    }

    [Fact]
    public void Home_shows_four_checklist_rows_and_the_empty_state_without_a_session()
    {
        using var rig = CreateRig();
        Sta.Run(() =>
        {
            var shell = rig.CreateShell();
            Visit(rig, shell, Section.Home);

            var rows = Sta.All<Button>(shell).Where(b => b.DataContext is ChecklistItemViewModel).ToList();
            Assert.Equal(4, rows.Count);
            Assert.All(rows, r => Assert.Contains(((ChecklistItemViewModel)r.DataContext).Title, Sta.SpokenName(r), StringComparison.Ordinal));
            var texts = Sta.All<TextBlock>(shell).Where(IsActuallyShown).Select(t => t.Text).ToList();
            Assert.Contains(UiText.Get("Session_None"), texts);
            Assert.Contains(UiText.Get("Home_NoSessionHint"), texts);
            Assert.Contains(Sta.All<Button>(shell), b => TextOf(b) == UiText.Get("Home_SeeAll"));
        });
    }

    [Fact]
    public void Pair_shows_six_digit_boxes_the_two_buttons_and_the_diagram()
    {
        using var rig = CreateRig();
        Sta.Run(() =>
        {
            var shell = rig.CreateShell();
            Visit(rig, shell, Section.Pair);

            var digits = Sta.All<Border>(shell).Count(b => b.Width == 58 && b.Height == 76);
            Assert.Equal(6, digits);
            var buttons = Sta.All<Button>(shell).Select(TextOf).ToList();
            Assert.Contains(UiText.Get("Pairing_Regenerate"), buttons);
            Assert.Contains(UiText.Get("Pair_Copy"), buttons);
            Assert.Equal(2, Sta.All<Line>(shell).Count());
            Assert.Contains(Sta.All<ItemsControl>(shell), c => Sta.SpokenName(c) == UiText.Format("Pair_CodeSpoken", "3 2 9 5 7 3"));
        });
    }

    [Fact]
    public void Window_shows_the_groups_the_three_buttons_and_the_send_keys_switch()
    {
        using var rig = CreateRig();
        Sta.Run(() =>
        {
            var shell = rig.CreateShell();
            Visit(rig, shell, Section.Window);

            var buttons = Sta.All<Button>(shell).Select(TextOf).ToList();
            Assert.Contains(UiText.Get("Target_Verify"), buttons);
            Assert.Contains(UiText.Get("Target_Diagnostic"), buttons);
            Assert.Contains(UiText.Get("Target_Refresh"), buttons);
            Assert.Contains(Sta.All<CheckBox>(shell), c => c.Content as string == UiText.Get("Toggle_SendKeys"));
            var expander = Sta.All<Expander>(shell).Single();
            Assert.Equal(UiText.Format("Window_Others", 2), expander.Header);
            Assert.False(expander.IsExpanded);
            var items = Sta.All<ListBoxItem>(shell).Where(IsActuallyShown).ToList();
            Assert.Equal(2, items.Count); // the suggested one and the other PowerPoint window; the rest hide in the collapsed expander
            Assert.True(((TargetRowViewModel)items[0].DataContext).Item.IsSuggested);
        });
    }

    [Fact]
    public void Window_without_a_window_system_still_offers_the_send_keys_switch()
    {
        using var rig = Rig.Create(withTarget: false, withAi: true);
        Sta.Run(() =>
        {
            var shell = rig.CreateShell();
            Visit(rig, shell, Section.Window);

            Assert.Contains(Sta.All<CheckBox>(shell).Where(IsActuallyShown), c => c.Content as string == UiText.Get("Toggle_SendKeys"));
            Assert.DoesNotContain(Sta.All<Button>(shell).Where(IsActuallyShown), b => TextOf(b) == UiText.Get("Target_Verify"));
        });
    }

    [Fact]
    public void Ai_shows_the_switch_the_status_the_stepper_and_the_three_markers()
    {
        using var rig = CreateRig();
        Sta.Run(() =>
        {
            var shell = rig.CreateShell();
            Visit(rig, shell, Section.Ai);

            Assert.Contains(Sta.All<CheckBox>(shell), c => c.Content as string == UiText.Get("Ai_Toggle"));
            var names = Sta.All<Button>(shell).Select(Sta.SpokenName).ToList();
            Assert.Contains(UiText.Get("Guide_Up"), names);
            Assert.Contains(UiText.Get("Guide_Down"), names);
            var texts = Sta.All<TextBlock>(shell).Where(IsActuallyShown).Select(t => t.Text).ToList();
            Assert.Contains(UiText.Get("Guide_RunningOut"), texts);
            Assert.Contains(UiText.Get("Guide_Critical"), texts);
            Assert.Contains(UiText.Get("Guide_Overtime"), texts);
            Assert.Contains(UiText.Get("Ai_Privacy"), texts);
        });
    }

    [Fact]
    public void Activity_and_Settings_show_their_controls()
    {
        using var rig = CreateRig();
        Sta.Run(() =>
        {
            var shell = rig.CreateShell();
            Visit(rig, shell, Section.Activity);
            Assert.Contains(UiText.Get("Events_Empty"), Sta.All<TextBlock>(shell).Where(IsActuallyShown).Select(t => t.Text));

            Visit(rig, shell, Section.Settings);
            var switches = Sta.All<CheckBox>(shell).Select(c => c.Content as string).ToList();
            Assert.Contains(UiText.Get("Pairing_Remember"), switches);
            Assert.Contains(UiText.Get("Toggle_LocalNetwork"), switches);
            var texts = Sta.All<TextBlock>(shell).Where(IsActuallyShown).Select(t => t.Text).ToList();
            Assert.Contains(UiText.Get("Settings_Tech"), texts);
            Assert.Contains(rig.ViewModel.PortText, texts);
            Assert.Contains(UiText.Format("Address_Emulator", rig.ViewModel.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)), texts);
        });
    }

    [Fact]
    public void The_server_toggle_is_on_every_page()
    {
        using var rig = CreateRig();
        Sta.Run(() =>
        {
            var shell = rig.CreateShell();
            foreach (var section in Enum.GetValues<Section>())
            {
                Visit(rig, shell, section);
                Assert.Contains(Sta.All<Button>(shell), b => TextOf(b) == rig.ViewModel.ToggleServerText);
            }
        });
    }

    [Fact]
    public void Each_page_is_a_different_view_chosen_by_its_page_view_model()
    {
        using var rig = CreateRig();
        Sta.Run(() =>
        {
            var shell = rig.CreateShell();
            var expected = new Dictionary<Section, Type>
            {
                [Section.Home] = typeof(HomePage),
                [Section.Pair] = typeof(PairPage),
                [Section.Window] = typeof(WindowPage),
                [Section.Ai] = typeof(AiPage),
                [Section.Activity] = typeof(ActivityPage),
                [Section.Settings] = typeof(SettingsPage),
            };
            foreach (var (section, type) in expected)
            {
                Visit(rig, shell, section);
                Assert.Single(Sta.All<UserControl>(shell), c => c.GetType() == type);
            }
        });
    }
}
