using System.Net;
using System.Net.Sockets;
using Presenta.Ai;
using Presenta.Domain;
using Presenta.Server;
using Presenta.Targeting;
using Presenta.TestSupport;

namespace Presenta.Companion.ViewModels.Tests;

/// <summary>Phase 11: the navigation shell, the checklist and the status dots, Inicio, the grouped window list and the didactic timeline.</summary>
public class DesktopRedesignTests
{
    private static readonly WindowInfo Show = FakeWindowSystem.Window(10, "Slide Show - deck.pptx", "POWERPNT", "screenClass");
    private static readonly WindowInfo Editing = FakeWindowSystem.Window(11, "deck.pptx - PowerPoint", "POWERPNT", "PPTFrameClass");
    private static readonly WindowInfo Notes = FakeWindowSystem.Window(12, "notes.txt - Notepad", "notepad", "Notepad");
    private static readonly WindowInfo Browser = FakeWindowSystem.Window(13, "News - Browser", "chrome", "Chrome_WidgetWin_1");

    private sealed class Rig : IAsyncDisposable
    {
        public ManualTimeProvider Time { get; } = new();

        public CompanionHost Host { get; }

        public MainViewModel ViewModel { get; }

        public FakeWindowSystem Windows { get; } = new();

        public RecordingClipboard Clipboard { get; } = new();

        public TargetSelectionViewModel? Target { get; }

        public AiRecommendationEnricher Enricher { get; }

        public Rig(
            bool withTarget = true,
            bool withAi = true,
            AiAvailability availability = AiAvailability.Available,
            CompanionServerOptions? options = null,
            IClipboard? clipboard = null,
            ITargetSettingsStore? store = null,
            params WindowInfo[] windows)
        {
            foreach (var window in windows)
            {
                Windows.Add(window);
            }

            Host = new CompanionHost(
                options ?? new CompanionServerOptions { Port = 0, PairingCode = "329573", HeartbeatTimeout = TimeSpan.FromHours(2) },
                new RecordingSlideKeyboard(),
                new InMemoryHistoryStore(),
                Time);
            Enricher = new AiRecommendationEnricher(
                availability == AiAvailability.Available ? new FakeAiTextClient() : null,
                availability,
                new AiEnrichmentOptions(),
                Time);
            Target = withTarget ? new TargetSelectionViewModel(new PresentationTargetService(Windows, store ?? new InMemoryTargetSettingsStore()), new ImmediateDispatcher(), Clipboard) : null;
            ViewModel = new MainViewModel(
                Host,
                new ImmediateDispatcher(),
                () => Array.Empty<string>(),
                maxEvents: MainViewModel.DefaultMaxEvents,
                timeZone: TimeZoneInfo.Utc,
                target: Target,
                ai: withAi ? new AiSectionViewModel(Enricher, new ImmediateDispatcher()) : null,
                clipboard: clipboard ?? Clipboard);
        }

        public async ValueTask DisposeAsync()
        {
            ViewModel.Dispose();
            await Host.DisposeAsync();
        }
    }

    private sealed class RecordingClipboard : IClipboard
    {
        public List<string> Texts { get; } = new();

        public void SetText(string text) => Texts.Add(text);
    }

    private sealed class ThrowingClipboard : IClipboard
    {
        public void SetText(string text) => throw new System.Runtime.InteropServices.ExternalException("busy");
    }

    private static async Task UntilAsync(Func<bool> condition, string because)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), "Timed out waiting for: " + because);
            await Task.Delay(10);
        }
    }

    // ---- navigation ----

    [Fact]
    public async Task The_window_starts_on_Inicio_with_exactly_one_selected_item()
    {
        await using var rig = new Rig();
        var navigation = rig.ViewModel.Navigation;

        Assert.Equal(Section.Home, navigation.Selected);
        Assert.IsType<HomePageViewModel>(navigation.CurrentPage);
        Assert.Single(navigation.Items, i => i.IsSelected);
        Assert.True(navigation.Items[(int)Section.Home].IsSelected);
    }

    [Theory]
    [InlineData(Section.Home, typeof(HomePageViewModel))]
    [InlineData(Section.Pair, typeof(PairPageViewModel))]
    [InlineData(Section.Window, typeof(WindowPageViewModel))]
    [InlineData(Section.Ai, typeof(AiPageViewModel))]
    [InlineData(Section.Activity, typeof(ActivityPageViewModel))]
    [InlineData(Section.Settings, typeof(SettingsPageViewModel))]
    public async Task Each_section_has_a_command_that_shows_its_page_and_marks_only_its_item(Section section, Type pageType)
    {
        await using var rig = new Rig();
        var navigation = rig.ViewModel.Navigation;
        navigation.Select(section == Section.Home ? Section.Settings : Section.Home); // start somewhere else

        navigation.Items[(int)section].Command.Execute(null);

        Assert.Equal(section, navigation.Selected);
        Assert.IsType(pageType, navigation.CurrentPage);
        Assert.Equal(section, navigation.CurrentPage.Section);
        Assert.Equal(new[] { section }, navigation.Items.Where(i => i.IsSelected).Select(i => i.Section));
    }

    [Fact]
    public async Task The_named_commands_match_the_sections()
    {
        await using var rig = new Rig();
        var navigation = rig.ViewModel.Navigation;
        var expected = new (System.Windows.Input.ICommand Command, Section Section)[]
        {
            (navigation.PairCommand, Section.Pair),
            (navigation.WindowCommand, Section.Window),
            (navigation.AiCommand, Section.Ai),
            (navigation.ActivityCommand, Section.Activity),
            (navigation.SettingsCommand, Section.Settings),
            (navigation.HomeCommand, Section.Home),
        };

        foreach (var (command, section) in expected)
        {
            command.Execute(null);
            Assert.Equal(section, navigation.Selected);
        }
    }

    [Fact]
    public async Task Configuracion_is_alone_at_the_bottom_and_the_other_five_keep_their_order()
    {
        await using var rig = new Rig();
        var navigation = rig.ViewModel.Navigation;

        Assert.Equal(new[] { Section.Home, Section.Pair, Section.Window, Section.Ai, Section.Activity }, navigation.TopItems.Select(i => i.Section));
        Assert.Equal(new[] { Section.Settings }, navigation.BottomItems.Select(i => i.Section));
        Assert.Equal(Enum.GetValues<Section>().Select(s => UiText.Get("Nav_" + s)), navigation.Items.Select(i => i.Label));
    }

    [Fact]
    public async Task Every_nav_item_and_page_has_a_name_from_the_resource_file()
    {
        await using var rig = new Rig();
        foreach (var item in rig.ViewModel.Navigation.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Label));
            Assert.False(string.IsNullOrWhiteSpace(item.AutomationName));
            Assert.False(string.IsNullOrWhiteSpace(rig.ViewModel.Navigation.Page(item.Section).Title));
        }
    }

    // ---- nav status dots ----

    [Fact]
    public async Task The_Vincular_dot_follows_the_watch_connection_and_a_server_error()
    {
        await using var rig = new Rig();
        var item = rig.ViewModel.Navigation.Items[(int)Section.Pair];
        Assert.Equal(NavStatus.Off, item.Status);
        Assert.Equal(UiText.Get("Watch_Waiting"), item.StatusText);

        await rig.ViewModel.StartAsync();
        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port, "329573");
        await UntilAsync(() => rig.ViewModel.WatchConnected, "the watch to connect");
        Assert.Equal(NavStatus.Ok, item.Status);
        Assert.Equal(UiText.Format("Nav_ItemWithStatus", item.Label, UiText.Get("Watch_Connected")), item.AutomationName);

        using var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        await using var busy = new Rig(options: new CompanionServerOptions { Port = ((IPEndPoint)blocker.LocalEndpoint).Port, PairingCode = "123456" });
        await busy.ViewModel.StartAsync();
        Assert.Equal(NavStatus.Warning, busy.ViewModel.Navigation.Items[(int)Section.Pair].Status);
    }

    [Fact]
    public async Task The_Ventana_dot_is_absent_without_a_window_system_off_when_nothing_is_chosen_ok_when_ready_and_warning_when_lost()
    {
        await using (var none = new Rig(withTarget: false))
        {
            Assert.Equal(NavStatus.None, none.ViewModel.Navigation.Items[(int)Section.Window].Status);
            Assert.False(none.ViewModel.Navigation.Items[(int)Section.Window].HasStatus);
        }

        await using var rig = new Rig(windows: new[] { Notes });
        var item = rig.ViewModel.Navigation.Items[(int)Section.Window];
        Assert.Equal(NavStatus.Off, item.Status); // nothing chosen

        rig.Windows.Add(Show);
        rig.Target!.RefreshList(); // a single slide show is chosen by itself (ADR-68), but not verified yet
        Assert.Equal(NavStatus.Warning, item.Status);
        Assert.Equal(UiText.Get("Check_Window_Unverified"), item.StatusText);

        await rig.Target.VerifyAsync();
        Assert.Equal(NavStatus.Ok, item.Status);
        Assert.Equal(rig.Target.StatusText, item.StatusText);

        rig.Windows.Remove(10);
        rig.Target.RefreshList();
        Assert.Equal(NavStatus.Warning, item.Status); // the chosen window is gone
    }

    [Fact]
    public async Task The_IA_dot_is_absent_without_the_section_off_when_off_ok_when_on_and_warning_for_a_problem()
    {
        await using (var none = new Rig(withAi: false))
        {
            Assert.Equal(NavStatus.None, none.ViewModel.Navigation.Items[(int)Section.Ai].Status);
        }

        await using var rig = new Rig();
        var item = rig.ViewModel.Navigation.Items[(int)Section.Ai];
        Assert.Equal(NavStatus.Off, item.Status);

        rig.ViewModel.Ai!.Enabled = true;
        Assert.Equal(NavStatus.Ok, item.Status);

        await using var problem = new Rig(availability: AiAvailability.InvalidAddress);
        Assert.Equal(NavStatus.Warning, problem.ViewModel.Navigation.Items[(int)Section.Ai].Status);
    }

    // ---- the checklist: each row in each state ----

    private static ChecklistItemViewModel Row(Rig rig, int index) => rig.ViewModel.Checklist[index];

    [Fact]
    public async Task The_checklist_has_four_rows_in_order_and_each_leads_to_its_page()
    {
        await using var rig = new Rig();
        var checklist = rig.ViewModel.Checklist;

        Assert.Equal(4, checklist.Count);
        Assert.Equal(new[] { Section.Pair, Section.Window, Section.Window, Section.Ai }, checklist.Select(r => r.Target));
        foreach (var row in checklist)
        {
            rig.ViewModel.Navigation.Select(Section.Settings);
            row.Command.Execute(null);
            Assert.Equal(row.Target, rig.ViewModel.Navigation.Selected);
        }
    }

    [Fact]
    public async Task Watch_row_empty_while_waiting_ok_when_connected_warning_on_a_server_error()
    {
        await using var rig = new Rig();
        Assert.Equal(CheckState.Empty, Row(rig, 0).State);
        Assert.Equal(UiText.Get("Watch_Waiting"), Row(rig, 0).Subtitle);

        await rig.ViewModel.StartAsync();
        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port, "329573");
        await UntilAsync(() => rig.ViewModel.WatchConnected, "the watch to connect");
        Assert.Equal(CheckState.Ok, Row(rig, 0).State);
        Assert.Equal(UiText.Get("Check_Watch_Linked"), Row(rig, 0).Subtitle);

        using var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        await using var busy = new Rig(options: new CompanionServerOptions { Port = ((IPEndPoint)blocker.LocalEndpoint).Port, PairingCode = "123456" });
        await busy.ViewModel.StartAsync();
        Assert.Equal(CheckState.Warning, Row(busy, 0).State);
        Assert.Equal(busy.ViewModel.ServerError, Row(busy, 0).Subtitle);
    }

    [Fact]
    public async Task Window_row_empty_with_nothing_chosen_ok_when_ready_warning_when_the_window_is_lost()
    {
        await using var rig = new Rig(windows: new[] { Editing, Notes });
        Assert.Equal(CheckState.Empty, Row(rig, 1).State); // nothing chosen, no slide show open

        rig.Windows.Add(Show);
        rig.Target!.RefreshList(); // the only slide show is chosen by itself, but not verified in this run
        Assert.Equal(CheckState.Warning, Row(rig, 1).State);
        Assert.Equal(UiText.Get("Check_Window_Unverified"), Row(rig, 1).Subtitle);

        await rig.Target.VerifyAsync();
        Assert.Equal(CheckState.Ok, Row(rig, 1).State);
        Assert.Equal(Show.Title, Row(rig, 1).Subtitle);

        rig.Windows.Remove(10);
        rig.Windows.Add(Show with { Handle = 30 }).Add(Show with { Handle = 31 });
        rig.Target.RefreshStatus();
        Assert.Equal(CheckState.Warning, Row(rig, 1).State);
        Assert.Equal(rig.Target.StatusText, Row(rig, 1).Subtitle);
    }

    // ---- Phase 13: a saved window is not "ready" until it is verified in this run ----

    [Fact]
    public async Task A_saved_but_unverified_window_shows_the_warning_mark_and_the_verify_hint()
    {
        var store = new InMemoryTargetSettingsStore(PresentationTargetIdentity.From(Show));
        await using var rig = new Rig(store: store, windows: new[] { Show, Editing, Notes });
        Assert.NotNull(rig.Target!.SelectedItem); // the saved choice resolves to a live window
        Assert.False(rig.Target.IsVerified);
        Assert.Equal(CheckState.Warning, Row(rig, 1).State);
        Assert.Equal(UiText.Get("Check_Window_Unverified"), Row(rig, 1).Subtitle);
        Assert.Equal(NavStatus.Warning, rig.ViewModel.Navigation.Items[(int)Section.Window].Status);
    }

    [Fact]
    public async Task Verifying_the_window_turns_the_row_and_the_dot_to_success_and_choosing_another_window_resets_it()
    {
        await using var rig = new Rig(windows: new[] { Show, Editing });
        Assert.Equal(CheckState.Warning, Row(rig, 1).State);

        await rig.Target!.VerifyAsync();
        Assert.True(rig.Target.IsVerified);
        Assert.Equal(CheckState.Ok, Row(rig, 1).State);
        Assert.Equal(NavStatus.Ok, rig.ViewModel.Navigation.Items[(int)Section.Window].Status);

        rig.Target.SelectedItem = rig.Target.Items.First(i => i.Window.Handle == Editing.Handle);
        Assert.False(rig.Target.IsVerified);
        Assert.Equal(CheckState.Warning, Row(rig, 1).State);
        Assert.Equal(NavStatus.Warning, rig.ViewModel.Navigation.Items[(int)Section.Window].Status);
    }

    [Fact]
    public async Task With_no_target_the_window_row_stays_empty_and_without_a_window_system_the_dot_is_absent()
    {
        await using var rig = new Rig(windows: new[] { Notes });
        Assert.False(rig.Target!.IsVerified);
        Assert.Equal(CheckState.Empty, Row(rig, 1).State);

        await using var none = new Rig(withTarget: false);
        Assert.Equal(CheckState.Empty, Row(none, 1).State);
        Assert.Equal(NavStatus.None, none.ViewModel.Navigation.Items[(int)Section.Window].Status);
    }

    [Fact]
    public async Task Keys_row_warning_when_off_empty_while_the_window_is_not_ready_ok_when_both_are_ready()
    {
        await using var rig = new Rig(windows: new[] { Notes });
        Assert.Equal(CheckState.Empty, Row(rig, 2).State);
        Assert.Equal(UiText.Get("Check_Keys_Waiting"), Row(rig, 2).Subtitle);

        rig.ViewModel.SendKeys = false;
        Assert.Equal(CheckState.Warning, Row(rig, 2).State);
        Assert.Equal(UiText.Get("Toggle_SendKeys_Hint"), Row(rig, 2).Subtitle);

        rig.ViewModel.SendKeys = true;
        rig.Windows.Add(Show);
        rig.Target!.RefreshList();
        Assert.Equal(CheckState.Ok, Row(rig, 2).State);
        Assert.Equal(UiText.Get("Check_Keys_On"), Row(rig, 2).Subtitle);
    }

    [Fact]
    public async Task Ai_row_empty_when_off_ok_when_on_warning_for_a_problem_and_it_shows_the_status_text()
    {
        await using var rig = new Rig();
        Assert.Equal(CheckState.Empty, Row(rig, 3).State);
        Assert.Equal(UiText.Get("Ai_Status_Disabled"), Row(rig, 3).Subtitle);

        rig.ViewModel.Ai!.Enabled = true;
        Assert.Equal(CheckState.Ok, Row(rig, 3).State);
        Assert.Equal(UiText.Get("Ai_Status_Ready"), Row(rig, 3).Subtitle);

        await using var problem = new Rig(availability: AiAvailability.InsecureAddress);
        Assert.Equal(CheckState.Warning, Row(problem, 3).State);

        await using var notConfigured = new Rig(availability: AiAvailability.MissingVariables);
        Assert.Equal(CheckState.Empty, Row(notConfigured, 3).State);

        await using var without = new Rig(withAi: false);
        Assert.Equal(CheckState.Empty, Row(without, 3).State);
    }

    [Fact]
    public async Task Every_row_reads_its_state_aloud_with_a_word_not_only_a_color()
    {
        await using var rig = new Rig();
        foreach (var state in Enum.GetValues<CheckState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(UiText.Get("Check_State_" + state)));
        }

        foreach (var row in rig.ViewModel.Checklist)
        {
            Assert.Contains(row.StateText, row.AutomationName, StringComparison.Ordinal);
            Assert.Contains(row.Title, row.AutomationName, StringComparison.Ordinal);
        }
    }

    // ---- Inicio: empty state and an active session ----

    [Fact]
    public async Task Inicio_shows_an_empty_state_with_no_session_and_no_figures()
    {
        await using var rig = new Rig();
        var vm = rig.ViewModel;

        Assert.True(vm.HasNoSession);
        Assert.False(vm.HasSession);
        Assert.Equal(0, vm.SessionCurrentSlide);
        Assert.Equal(0, vm.SessionTotalSlides);
        Assert.Equal(string.Empty, vm.SessionElapsedText);
        Assert.Equal(string.Empty, vm.PaceText);
        Assert.Equal(0, vm.SessionProgressPercent);
        Assert.True(vm.HasNoEntries);
        Assert.False(vm.HasRecentEntries);
    }

    [Fact]
    public async Task Inicio_shows_the_live_figures_of_an_active_session_and_clears_them_afterwards()
    {
        await using var rig = new Rig();
        await rig.ViewModel.StartAsync();
        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port, "329573");

        await watch.CommandAsync("session.start", """{ "plannedSlides": 10, "plannedDurationMs": 600000, "name": "Demo" }""");
        await watch.CommandAsync("slide.next");
        rig.Time.Advance(TimeSpan.FromSeconds(30));
        await UntilAsync(() => rig.ViewModel.SessionElapsedText == "00:30", "the elapsed time to update");

        var vm = rig.ViewModel;
        Assert.True(vm.HasSession);
        Assert.Equal(2, vm.SessionCurrentSlide);
        Assert.Equal(10, vm.SessionTotalSlides);
        Assert.Equal("09:30", vm.SessionRemainingClock);
        Assert.Equal(5.0, vm.SessionProgressPercent, 3);
        Assert.False(string.IsNullOrEmpty(vm.PaceText));
        Assert.Equal(UiText.Format("Home_RingName", 5), vm.SessionRingName);

        await watch.CommandAsync("session.finish");
        await UntilAsync(() => vm.HasNoSession, "the session to clear");
        Assert.Equal(0, vm.SessionProgressPercent);
        Assert.Equal(string.Empty, vm.PaceText);
    }

    [Fact]
    public async Task The_ring_is_full_in_overtime_and_the_pace_has_a_word_and_a_tone()
    {
        await using var rig = new Rig();
        await rig.ViewModel.StartAsync();
        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port, "329573");
        await watch.CommandAsync("session.start", """{ "plannedSlides": 5, "plannedDurationMs": 60000 }""");

        rig.Time.Advance(TimeSpan.FromSeconds(75));

        await UntilAsync(() => rig.ViewModel.SessionOvertime, "overtime");
        Assert.Equal(100, rig.ViewModel.SessionProgressPercent);
        Assert.NotEqual(PaceTone.Good, rig.ViewModel.PaceTone); // on slide 1 of 5 after 75 s the presenter is behind
        Assert.Equal(UiText.Get("Pace_Behind"), rig.ViewModel.PaceText);
    }

    [Fact]
    public async Task Recent_activity_keeps_the_newest_three_and_the_full_list_keeps_more_with_tones()
    {
        await using var rig = new Rig();
        await rig.ViewModel.StartAsync();
        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port, "329573");
        await watch.CommandAsync("session.start", """{ "plannedSlides": 10, "plannedDurationMs": 600000 }""");
        await watch.CommandAsync("slide.next");
        await watch.CommandAsync("slide.next");
        await watch.CommandAsync("session.pause");
        await UntilAsync(() => rig.ViewModel.Events.Count >= 5, "five events");

        var vm = rig.ViewModel;
        Assert.Equal(3, vm.RecentEntries.Count);
        Assert.True(vm.ActivityEntries.Count >= 5);
        Assert.Equal(vm.ActivityEntries.Take(3), vm.RecentEntries);
        Assert.Equal(vm.Events, vm.ActivityEntries.Select(e => e.Line)); // the same lines as before
        Assert.Equal(ActivityTone.Warning, vm.ActivityEntries[0].Tone); // paused
        Assert.Contains(vm.ActivityEntries, e => e.Tone == ActivityTone.Good);
        Assert.False(vm.HasNoEntries);
    }

    // ---- Vincular ----

    [Fact]
    public async Task The_code_is_split_into_six_digits_that_follow_a_new_code()
    {
        await using var rig = new Rig();
        var vm = rig.ViewModel;
        Assert.Equal(new[] { "3", "2", "9", "5", "7", "3" }, vm.PairingDigits);
        Assert.Equal(UiText.Format("Pair_CodeSpoken", "3 2 9 5 7 3"), vm.PairingCodeSpoken);

        vm.RegenerateCodeCommand.Execute(null);

        Assert.Equal(vm.PairingCode.Select(c => c.ToString()), vm.PairingDigits);
        Assert.Equal(6, vm.PairingDigits.Count);
    }

    [Fact]
    public async Task Copying_the_code_puts_it_on_the_clipboard_and_says_so_until_the_code_changes()
    {
        await using var rig = new Rig();
        var vm = rig.ViewModel;
        Assert.False(vm.HasCopyFeedback);

        vm.CopyCodeCommand.Execute(null);

        Assert.Equal(new[] { "329573" }, rig.Clipboard.Texts);
        Assert.Equal(UiText.Get("Pair_Copied"), vm.CopyFeedback);
        vm.RegenerateCodeCommand.Execute(null);
        Assert.False(vm.HasCopyFeedback);
    }

    [Fact]
    public async Task A_clipboard_that_fails_is_reported_and_never_throws()
    {
        await using var rig = new Rig(clipboard: new ThrowingClipboard());

        rig.ViewModel.CopyCodeCommand.Execute(null);

        Assert.Equal(UiText.Get("Pair_CopyFailed"), rig.ViewModel.CopyFeedback);
    }

    // ---- Ventana: grouping ----

    [Fact]
    public async Task The_list_is_grouped_suggested_first_other_powerpoint_next_and_the_rest_behind_the_expander()
    {
        await using var rig = new Rig(windows: new[] { Notes, Browser, Editing, Show });
        var list = rig.ViewModel.TargetList!;

        Assert.Equal(new[] { Show.Handle }, list.Suggested.Select(r => r.Item.Window.Handle));
        Assert.Equal(new[] { Editing.Handle }, list.Presentation.Select(r => r.Item.Window.Handle));
        Assert.Equal(new[] { Notes.Handle, Browser.Handle }.Order(), list.Others.Select(r => r.Item.Window.Handle).Order());
        Assert.Equal(UiText.Format("Target_ItemSuggested", Show.Title, Show.ProcessName), list.Suggested[0].Display);
        Assert.Equal(UiText.Format("Window_Others", 2), list.OthersLabel);
        Assert.False(list.OthersExpanded);
        Assert.True(list.HasSuggested && list.HasPresentation && list.HasOthers);
    }

    [Fact]
    public async Task Grouping_loses_no_window_and_repeats_none()
    {
        await using var rig = new Rig(windows: new[] { Notes, Browser, Editing, Show });
        var list = rig.ViewModel.TargetList!;

        void Check()
        {
            var grouped = list.Suggested.Concat(list.Presentation).Concat(list.Others).Select(r => r.Item).ToList();
            Assert.Equal(rig.Target!.Items.Count, grouped.Count);
            Assert.Equal(rig.Target.Items.OrderBy(i => i.Window.Handle), grouped.OrderBy(i => i.Window.Handle));
            Assert.Equal(grouped.Count, grouped.Distinct().Count());
        }

        Check();
        rig.Windows.Add(FakeWindowSystem.Window(14, "Chat", "chat", "ChatWindow"));
        rig.Target!.RefreshList();
        Check();
        Assert.Equal(3, list.Others.Count);
        rig.Windows.Remove(12);
        rig.Target.RefreshList();
        Check();
    }

    [Fact]
    public async Task Choosing_a_window_in_any_group_goes_through_the_same_selection_and_a_choice_in_the_expander_opens_it()
    {
        await using var rig = new Rig(windows: new[] { Show, Editing, Notes, Browser });
        var list = rig.ViewModel.TargetList!;
        var service = rig.Target!;

        list.Others[0].IsChosen = true;
        Assert.Same(list.Others[0].Item, service.SelectedItem);
        Assert.True(list.OthersExpanded);
        Assert.True(list.Others[0].IsChosen);
        Assert.All(list.Suggested.Concat(list.Presentation), r => Assert.False(r.IsChosen));
        Assert.Equal(1, list.Suggested.Concat(list.Presentation).Concat(list.Others).Count(r => r.IsChosen));

        list.Suggested[0].IsChosen = true;
        Assert.Same(list.Suggested[0].Item, service.SelectedItem);
        Assert.False(list.Others[0].IsChosen);

        list.Suggested[0].IsChosen = false; // a control deselecting does nothing
        Assert.Same(list.Suggested[0].Item, service.SelectedItem);
    }

    [Fact]
    public async Task Without_a_window_system_there_is_no_list()
    {
        await using var rig = new Rig(withTarget: false);

        Assert.Null(rig.ViewModel.TargetList);
        Assert.False(((WindowPageViewModel)rig.ViewModel.Navigation.Page(Section.Window)).HasList);
    }

    // ---- IA y alertas: the didactic markers come from the domain ----

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(15)]
    [InlineData(60)]
    public void The_markers_equal_what_the_domain_computes(int minutes)
    {
        var options = new SessionOptions();
        var planned = TimeSpan.FromMinutes(minutes);
        var guide = new AlertGuideViewModel(options) { Minutes = minutes };

        Assert.Equal(planned, guide.Planned);
        Assert.Equal(planned - options.GetTimeRunningOutThreshold(planned), guide.RunningOutAt);
        Assert.Equal(planned - options.GetTimeCriticalThreshold(planned), guide.CriticalAt);
        Assert.Equal(planned, guide.OvertimeAt);
        Assert.Equal(options.GetTimeRunningOutThreshold(planned) > options.GetTimeCriticalThreshold(planned), guide.HasRunningOutMarker);
        Assert.Equal(guide.HasRunningOutMarker ? 3 : 2, guide.Markers.Count);
        Assert.Equal(1.0, guide.CalmFraction + guide.RunningOutFraction + guide.CriticalFraction, 6);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(15)]
    [InlineData(60)]
    public void A_real_session_raises_each_alert_at_the_time_of_its_marker(int minutes)
    {
        var guide = new AlertGuideViewModel { Minutes = minutes };
        var time = new ManualTimeProvider();
        var session = new PresentationSession(SessionConfiguration.Create(10, guide.Planned).Value!, time);
        Assert.True(session.Start().Succeeded);
        var millisecond = TimeSpan.FromMilliseconds(1);

        if (guide.HasRunningOutMarker)
        {
            time.Advance(guide.RunningOutAt - millisecond);
            Assert.NotEqual(AlertKind.TimeRunningOut, session.Tick()?.Kind);
            time.Advance(millisecond);
            Assert.Equal(AlertKind.TimeRunningOut, session.Tick()?.Kind);
            time.Advance(guide.CriticalAt - guide.RunningOutAt - millisecond);
        }
        else
        {
            time.Advance(guide.CriticalAt - millisecond);
        }

        Assert.NotEqual(AlertKind.TimeCritical, session.Tick()?.Kind);
        time.Advance(millisecond);
        Assert.Equal(AlertKind.TimeCritical, session.Tick()?.Kind);

        time.Advance(guide.OvertimeAt - guide.CriticalAt - millisecond);
        Assert.NotEqual(AlertKind.Overtime, session.Tick()?.Kind);
        time.Advance(millisecond * 2);
        Assert.Equal(AlertKind.Overtime, session.Tick()?.Kind);
    }

    [Fact]
    public void The_guide_follows_a_custom_set_of_options_and_not_a_copy_of_the_numbers()
    {
        var options = new SessionOptions { TimeCritical = TimeSpan.FromSeconds(30), TimeRunningOutMax = TimeSpan.FromMinutes(2) };
        var guide = new AlertGuideViewModel(options) { Minutes = 15 };

        Assert.Equal(TimeSpan.FromMinutes(13), guide.RunningOutAt);
        Assert.Equal(TimeSpan.FromSeconds(14 * 60 + 30), guide.CriticalAt);
    }

    [Fact]
    public void The_stepper_stays_between_one_and_sixty_and_starts_at_fifteen()
    {
        var guide = new AlertGuideViewModel();
        Assert.Equal(15, guide.Minutes);

        guide.Minutes = 0;
        Assert.Equal(1, guide.Minutes);
        Assert.False(guide.CanStepDown);
        guide.StepDownCommand.Execute(null);
        Assert.Equal(1, guide.Minutes);

        guide.Minutes = 99;
        Assert.Equal(60, guide.Minutes);
        Assert.False(guide.CanStepUp);
        guide.StepUpCommand.Execute(null);
        Assert.Equal(60, guide.Minutes);

        guide.Minutes = 14;
        guide.StepUpCommand.Execute(null);
        Assert.Equal(15, guide.Minutes);
    }

    [Fact]
    public void Markers_have_their_own_rows_and_stay_inside_the_bar()
    {
        var guide = new AlertGuideViewModel { Minutes = 60 };

        Assert.Equal(new[] { 0, 1, 2 }, guide.Markers.Select(m => m.Row));
        Assert.All(guide.Markers, m => Assert.InRange(m.Position, 0.08, 0.92));
        Assert.All(guide.Markers, m => Assert.Contains(m.Label, m.AutomationName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_guide_never_touches_the_server_or_a_session()
    {
        await using var rig = new Rig();
        await rig.ViewModel.StartAsync();
        var before = rig.Host.Service.Status;

        rig.ViewModel.Guide.StepUpCommand.Execute(null);
        rig.ViewModel.Guide.Minutes = 40;

        Assert.Null(rig.Host.Service.Status.Session);
        Assert.Equal(before.PairingCode, rig.Host.Service.Status.PairingCode);
        Assert.Equal(rig.Host.Service.Status.SendKeys, before.SendKeys);
    }

    // ---- every new string exists ----

    [Fact]
    public void Every_string_of_the_new_pages_exists_in_the_resource_file()
    {
        var keys = new[]
        {
            "Nav_Label", "Nav_ItemWithStatus", "Home_Title", "Home_Lead", "Home_Checklist", "Home_Session", "Home_SlideCaption", "Home_ElapsedCaption",
            "Home_RemainingCaption", "Home_OvertimeCaption", "Home_NoSessionHint", "Home_RingName", "Home_SeeAll", "Check_Keys", "Check_Keys_On",
            "Check_Keys_Waiting", "Check_Watch_Linked", "Check_Item", "Pace_OnTrack", "Pace_Behind", "Pace_TooFast", "Pace_NotEnoughData", "Pair_Title",
            "Pair_Lead", "Pair_CodeSpoken", "Pair_Copy", "Pair_Copied", "Pair_CopyFailed", "Pair_NodeComputer", "Pair_DiagramName", "Window_Others",
            "Window_ListName", "Window_OthersListName", "Ai_PageLead", "Guide_Title", "Guide_Tag", "Guide_Minutes", "Guide_Up", "Guide_Down", "Guide_Value",
            "Guide_BarName", "Guide_RunningOut", "Guide_Critical", "Guide_Overtime", "Guide_Marker", "Guide_Note", "Activity_Lead", "Settings_Lead", "Settings_Tech",
        };
        foreach (var key in keys.Concat(Enum.GetValues<Section>().Select(s => "Nav_" + s)).Concat(Enum.GetValues<CheckState>().Select(s => "Check_State_" + s)))
        {
            Assert.False(string.IsNullOrWhiteSpace(UiText.Get(key)), key);
        }

        foreach (var status in Enum.GetValues<PaceStatus>())
        {
            Assert.False(string.IsNullOrWhiteSpace(UiText.Get("Pace_" + status)), status.ToString());
        }
    }
}
