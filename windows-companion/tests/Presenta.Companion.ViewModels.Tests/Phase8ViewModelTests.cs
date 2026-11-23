using Presenta.Domain.Keyboard;
using Presenta.Server;
using Presenta.Targeting;
using Presenta.TestSupport;
using static Presenta.TestSupport.FakeWindowSystem;

namespace Presenta.Companion.ViewModels.Tests;

/// <summary>Phase 8, item 1: automatic choice of the only slide show, and the warning for other PowerPoint windows (ADR-68).</summary>
public class AutomaticTargetTests
{
    private static readonly WindowInfo Show = Window(10, "Slide Show - deck.pptx", "POWERPNT", "screenClass");
    private static readonly WindowInfo OtherShow = Window(12, "Slide Show - other.pptx", "POWERPNT", "screenClass");
    private static readonly WindowInfo Editing = Window(13, "deck.pptx - PowerPoint", "POWERPNT", "PPTFrameClass");
    private static readonly WindowInfo Notes = Window(11, "notes.txt - Notepad", "notepad", "Notepad");
    private static readonly WindowInfo Browser = Window(14, "Slides in a browser - Chrome", "chrome", "Chrome_WidgetWin_1");
    private static readonly WindowInfo Studio = Window(20, "Studio", "studio", "Frame");

    private static (TargetSelectionViewModel Vm, PresentationTargetService Service, FakeWindowSystem Windows, InMemoryTargetSettingsStore Store) Create(
        PresentationTargetIdentity? stored,
        params WindowInfo[] windows)
    {
        var system = new FakeWindowSystem();
        foreach (var window in windows)
        {
            system.Add(window);
        }

        var store = new InMemoryTargetSettingsStore(stored);
        var service = new PresentationTargetService(system, store);
        return (new TargetSelectionViewModel(service, new ImmediateDispatcher()), service, system, store);
    }

    [Fact]
    public void A_single_slide_show_is_chosen_by_itself_and_the_status_says_so()
    {
        var (vm, service, windows, store) = Create(null, Notes, Show, Studio);

        Assert.Equal(Show.Handle, vm.SelectedItem!.Window.Handle);
        Assert.Equal(PresentationTargetIdentity.From(Show), service.Identity);
        Assert.Equal(service.Identity, store.Identity);
        Assert.Equal(UiText.Get("Target_Status_AutoSelected"), vm.StatusText);
        Assert.Equal(TargetSeverity.Good, vm.Severity);
        Assert.False(vm.HasNotSlideShowWarning);
        Assert.Empty(windows.Keys);
    }

    [Fact]
    public void Choosing_by_itself_never_verifies_so_it_never_touches_the_window_or_the_keyboard()
    {
        var (_, _, windows, _) = Create(null, Show, Studio);

        Assert.DoesNotContain(windows.Calls, c => c.StartsWith("activate", StringComparison.Ordinal) || c.StartsWith("key", StringComparison.Ordinal));
    }

    [Fact]
    public void Several_slide_shows_choose_nothing_and_the_status_asks_to_pick_one()
    {
        var (vm, service, _, store) = Create(null, Show, OtherShow, Notes);

        Assert.Null(vm.SelectedItem);
        Assert.Null(service.Identity);
        Assert.Null(store.Identity);
        Assert.Equal(UiText.Get("Target_Status_ChooseOne"), vm.StatusText);
        Assert.Equal(TargetSeverity.Neutral, vm.Severity);
    }

    [Fact]
    public void Without_any_slide_show_nothing_is_chosen_even_if_other_windows_exist()
    {
        var (vm, service, _, _) = Create(null, Notes, Editing, Browser);

        Assert.Null(vm.SelectedItem);
        Assert.Null(service.Identity);
        Assert.Equal(UiText.Get("Target_Status_None"), vm.StatusText);
    }

    [Fact]
    public void A_stored_window_that_cannot_be_found_is_replaced_by_the_only_slide_show()
    {
        var gone = PresentationTargetIdentity.From(Window(99, "old deck - Notepad", "notepad", "Notepad"));
        var (vm, service, _, store) = Create(gone, Show, Studio);

        Assert.Equal(Show.Handle, vm.SelectedItem!.Window.Handle);
        Assert.Equal(PresentationTargetIdentity.From(Show), store.Identity);
        Assert.Equal(PresentationTargetIdentity.From(Show), service.Identity);
        Assert.Equal(UiText.Get("Target_Status_AutoSelected"), vm.StatusText);
    }

    [Fact]
    public void A_stored_window_that_cannot_be_found_with_several_slide_shows_keeps_the_stored_choice_and_asks_to_pick()
    {
        var gone = PresentationTargetIdentity.From(Window(99, "old deck - Notepad", "notepad", "Notepad"));
        var (vm, service, _, store) = Create(gone, Show, OtherShow);

        Assert.Null(vm.SelectedItem);
        Assert.Equal(gone, service.Identity);
        Assert.Equal(gone, store.Identity);
        Assert.Equal(UiText.Get("Target_Status_ChooseOne"), vm.StatusText);
    }

    [Fact]
    public void A_stored_window_that_still_resolves_is_never_replaced()
    {
        var (vm, service, _, store) = Create(PresentationTargetIdentity.From(Notes), Notes, Show);

        Assert.Equal(Notes.Handle, vm.SelectedItem!.Window.Handle);
        Assert.Equal(PresentationTargetIdentity.From(Notes), service.Identity);
        Assert.Equal(PresentationTargetIdentity.From(Notes), store.Identity);
        Assert.Equal(UiText.Get("Target_Status_Ready"), vm.StatusText);
    }

    [Fact]
    public void Choosing_another_row_by_hand_ends_the_automatic_note_and_saves_the_hand_choice()
    {
        var (vm, service, _, _) = Create(null, Show, Notes);
        Assert.Equal(UiText.Get("Target_Status_AutoSelected"), vm.StatusText);

        vm.SelectedItem = vm.Items.Single(i => i.Window.Handle == Notes.Handle);

        Assert.Equal(PresentationTargetIdentity.From(Notes), service.Identity);
        Assert.Equal(UiText.Get("Target_Status_Ready"), vm.StatusText);
    }

    [Fact]
    public void Selecting_a_row_never_runs_verify_by_itself()
    {
        var (vm, _, windows, _) = Create(null, Notes, Show, OtherShow);
        windows.Foreground = Studio.Handle;

        vm.SelectedItem = vm.Items.First();

        Assert.DoesNotContain(windows.Calls, c => c.StartsWith("activate", StringComparison.Ordinal));
        Assert.Equal(Studio.Handle, windows.Foreground);
        Assert.Empty(windows.Keys);
    }

    [Fact]
    public void Refreshing_keeps_the_current_choice_when_it_still_resolves_even_if_a_slide_show_appears()
    {
        var (vm, service, windows, _) = Create(null, Notes);
        vm.SelectedItem = vm.Items.Single();
        var chosen = service.Identity;

        windows.Add(Show);
        vm.RefreshCommand.Execute(null);

        Assert.Equal(Notes.Handle, vm.SelectedItem!.Window.Handle);
        Assert.Equal(chosen, service.Identity);
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public void Refreshing_keeps_an_automatic_choice_and_its_note_while_it_still_resolves()
    {
        var (vm, service, windows, _) = Create(null, Show);
        var chosen = service.Identity;

        windows.Add(Notes);
        vm.RefreshList();

        Assert.Equal(Show.Handle, vm.SelectedItem!.Window.Handle);
        Assert.Equal(chosen, service.Identity);
        Assert.Equal(UiText.Get("Target_Status_AutoSelected"), vm.StatusText);
    }

    [Fact]
    public void A_presentation_that_starts_later_is_picked_up_by_the_timer_without_pressing_refresh()
    {
        var (vm, service, windows, _) = Create(null, Notes);
        Assert.Null(service.Identity);

        windows.Add(Show);
        vm.Tick();

        Assert.Equal(Show.Handle, vm.SelectedItem!.Window.Handle);
        Assert.Equal(PresentationTargetIdentity.From(Show), service.Identity);
        Assert.Equal(UiText.Get("Target_Status_AutoSelected"), vm.StatusText);
    }

    [Fact]
    public void The_timer_does_not_rebuild_the_list_when_nothing_changed()
    {
        var (vm, _, _, _) = Create(null, Notes, Browser);
        var first = vm.Items[0];

        vm.Tick();
        vm.Tick();

        Assert.Same(first, vm.Items[0]);
    }

    [Fact]
    public void The_timer_never_changes_a_choice_that_still_resolves()
    {
        var (vm, service, windows, _) = Create(PresentationTargetIdentity.From(Notes), Notes);
        var chosen = service.Identity;

        windows.Add(Show);
        vm.Tick();

        Assert.Equal(chosen, service.Identity);
        Assert.Equal(Notes.Handle, vm.SelectedItem!.Window.Handle);
    }

    // ---- the warning ----

    [Fact]
    public void A_powerpoint_window_that_is_not_the_slide_show_shows_the_warning_next_to_the_status()
    {
        var (vm, _, _, _) = Create(PresentationTargetIdentity.From(Editing), Editing, Notes);

        Assert.True(vm.HasNotSlideShowWarning);
        Assert.Equal(UiText.Get("Target_Warning_NotSlideShow"), vm.NotSlideShowWarning);
        Assert.Equal(UiText.Get("Target_Status_Ready"), vm.StatusText); // sending is still governed by the same rules
    }

    [Theory]
    [InlineData("show")]
    [InlineData("notepad")]
    [InlineData("browser")]
    [InlineData("nothing")]
    public void The_warning_is_absent_for_the_slide_show_other_programs_and_no_choice(string kind)
    {
        var stored = kind switch
        {
            "show" => PresentationTargetIdentity.From(Show),
            "notepad" => PresentationTargetIdentity.From(Notes),
            "browser" => PresentationTargetIdentity.From(Browser),
            _ => null,
        };
        var (vm, _, _, _) = Create(stored, Show, OtherShow, Notes, Browser);

        Assert.False(vm.HasNotSlideShowWarning);
        Assert.Null(vm.NotSlideShowWarning);
    }

    [Fact]
    public void The_warning_goes_away_when_the_user_chooses_the_slide_show_and_returns_when_the_frame_is_chosen_again()
    {
        var (vm, _, _, _) = Create(PresentationTargetIdentity.From(Editing), Editing, Show, OtherShow);
        Assert.True(vm.HasNotSlideShowWarning);

        vm.SelectedItem = vm.Items.Single(i => i.Window.Handle == Show.Handle);
        Assert.False(vm.HasNotSlideShowWarning);

        vm.SelectedItem = vm.Items.Single(i => i.Window.Handle == Editing.Handle);
        Assert.True(vm.HasNotSlideShowWarning);
    }

    [Fact]
    public void The_warning_text_is_spanish_and_names_the_suggested_entry()
    {
        var text = UiText.Get("Target_Warning_NotSlideShow");

        var suggestedWord = UiText.Get("Target_ItemSuggested").Split(':')[0]; // the word the list entry starts with
        Assert.Contains(suggestedWord, text, StringComparison.Ordinal);
        Assert.NotEqual("Target_Warning_NotSlideShow", text);
    }
}

/// <summary>Phase 8, items 2, 3 and 5 as seen by the window: recovery line, remembered code switch, single instance.</summary>
public class Phase8WindowTests
{
    private static string TempFile(string name) =>
        Path.Combine(Path.GetTempPath(), "presenta-tests-" + Guid.NewGuid().ToString("N"), name);

    private static CompanionHost Host(IActiveSessionStore? sessions = null, ICompanionSettingsStore? settings = null) =>
        new(
            new CompanionServerOptions { Port = 0, PairingCode = null },
            new RecordingSlideKeyboard(),
            new InMemoryHistoryStore(),
            new ManualTimeProvider(),
            activeSessions: sessions,
            settings: settings);

    [Fact]
    public async Task A_recovered_session_adds_exactly_one_spanish_activity_line()
    {
        var sessions = new InMemoryActiveSessionStore
        {
            Record = new ActiveSessionRecord("Orphan", 20, 900_000, 7, 125_000, false, new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.Zero)),
        };
        await using var host = Host(sessions);
        using var vm = new MainViewModel(host, new ImmediateDispatcher(), lanAddresses: () => Array.Empty<string>());

        await vm.StartAsync();

        var line = Assert.Single(vm.Events);
        Assert.Contains(UiText.Get("Event_SessionRecovered"), line, StringComparison.Ordinal);
        Assert.False(vm.HasSession); // it is not resumed

        await vm.StopAsync();
        await vm.StartAsync(); // starting again later does not repeat it
        Assert.Single(vm.Events);
    }

    [Fact]
    public async Task The_remember_switch_is_off_by_default_follows_the_service_and_stores_the_code_when_turned_on()
    {
        var settings = new InMemoryCompanionSettingsStore();
        await using var host = Host(settings: settings);
        using var vm = new MainViewModel(host, new ImmediateDispatcher(), lanAddresses: () => Array.Empty<string>());
        Assert.False(vm.RememberPairingCode);

        vm.RememberPairingCode = true;

        Assert.True(host.Service.RememberPairingCode);
        Assert.Equal(new CompanionSettings(true, vm.PairingCode), settings.Current);

        vm.RegenerateCodeCommand.Execute(null);
        Assert.Equal(new CompanionSettings(true, vm.PairingCode), settings.Current);

        vm.RememberPairingCode = false;
        Assert.Equal(CompanionSettings.Default, settings.Current);
    }

    [Fact]
    public async Task The_window_starts_with_the_remembered_state_and_code_of_the_settings()
    {
        var settings = new InMemoryCompanionSettingsStore(new CompanionSettings(true, "246810"));
        await using var host = Host(settings: settings);

        using var vm = new MainViewModel(host, new ImmediateDispatcher(), lanAddresses: () => Array.Empty<string>());

        Assert.True(vm.RememberPairingCode);
        Assert.Equal("246810", vm.PairingCode);
    }

    [Fact]
    public void The_new_captions_are_spanish_and_come_from_the_resources()
    {
        var labels = new UiLabels();

        Assert.Equal(UiText.Get("Pairing_Remember"), labels.RememberCodeToggle);
        Assert.Equal(UiText.Get("Pairing_Remember_Hint"), labels.RememberCodeHint);
        Assert.All(
            new[] { "Pairing_Remember", "Pairing_Remember_Hint", "SingleInstance_Message", "Event_SessionRecovered", "Target_Status_AutoSelected", "Target_Status_ChooseOne", "Target_Warning_NotSlideShow" },
            key => Assert.NotEqual(key, UiText.Get(key)));
    }

    // ---- single instance ----

    private sealed class FakeLock : IInstanceLock
    {
        public FakeLock(bool free) => Free = free;

        public bool Free { get; }

        public int Attempts { get; private set; }

        public bool TryAcquire()
        {
            Attempts++;
            return Free;
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void The_second_instance_shows_the_spanish_message_and_must_not_start_anything()
    {
        var messages = new List<string>();
        var started = false;

        var enter = SingleInstanceGate.TryEnter(new FakeLock(free: false), messages.Add);
        if (enter)
        {
            started = true; // stands for creating the server, the window and the files
        }

        Assert.False(enter);
        Assert.False(started);
        var message = Assert.Single(messages);
        Assert.Equal(UiText.Get("SingleInstance_Message"), message);
        Assert.StartsWith("Presenta", message, StringComparison.Ordinal); // the product name
    }

    [Fact]
    public void The_first_instance_continues_without_any_message()
    {
        var messages = new List<string>();

        var enter = SingleInstanceGate.TryEnter(new FakeLock(free: true), messages.Add);

        Assert.True(enter);
        Assert.Empty(messages);
    }

    [Fact]
    public void The_named_lock_admits_one_owner_per_name_and_frees_it_on_dispose()
    {
        var name = @"Local\Presenta.Tests." + Guid.NewGuid().ToString("N");
        using var first = new NamedEventInstanceLock(name);
        using var second = new NamedEventInstanceLock(name);

        Assert.True(first.TryAcquire());
        Assert.True(first.TryAcquire()); // asking again is harmless
        Assert.False(second.TryAcquire());

        first.Dispose();

        using var third = new NamedEventInstanceLock(name);
        Assert.True(third.TryAcquire());
        Assert.False(second.TryAcquire());
    }

    [Fact]
    public void Different_names_do_not_block_each_other_and_the_real_name_is_scoped_to_the_user_session()
    {
        using var a = new NamedEventInstanceLock(@"Local\Presenta.Tests." + Guid.NewGuid().ToString("N"));
        using var b = new NamedEventInstanceLock(@"Local\Presenta.Tests." + Guid.NewGuid().ToString("N"));

        Assert.True(a.TryAcquire());
        Assert.True(b.TryAcquire());
        Assert.StartsWith(@"Local\", NamedEventInstanceLock.DefaultName, StringComparison.Ordinal);
    }

    // ---- launch arguments ----

    [Fact]
    public void The_launch_arguments_accept_the_new_file_options_and_ignore_empty_values()
    {
        var parsed = LaunchArguments.Parse(new[] { "--settings", @"C:\t\settings.json", "--active-session", @"C:\t\active.json", "--port", "9" });

        Assert.Equal(@"C:\t\settings.json", parsed.SettingsPath);
        Assert.Equal(@"C:\t\active.json", parsed.ActiveSessionPath);
        Assert.Equal(9, parsed.Port);
        Assert.Null(LaunchArguments.Parse(new[] { "--settings", " " }).SettingsPath);
        Assert.Null(LaunchArguments.Parse(Array.Empty<string>()).ActiveSessionPath);
    }

    [Fact]
    public async Task The_real_files_recover_a_session_and_keep_the_code_through_two_runs_of_the_window()
    {
        var sessionFile = TempFile("active-session.json");
        var settingsFile = TempFile("settings.json");
        try
        {
            var sessions = new JsonActiveSessionStore(sessionFile);
            Assert.True(sessions.Save(new ActiveSessionRecord("Orphan", 10, 600_000, 3, 90_000, true, DateTimeOffset.UtcNow)));
            string code;
            await using (var first = Host(sessions, new JsonCompanionSettingsStore(settingsFile)))
            {
                using var vm = new MainViewModel(first, new ImmediateDispatcher(), lanAddresses: () => Array.Empty<string>());
                vm.RememberPairingCode = true;
                code = vm.PairingCode;
                await vm.StartAsync();
                Assert.Single(vm.Events);
                await vm.StopAsync();
            }

            Assert.False(File.Exists(sessionFile));
            await using var second = Host(sessions, new JsonCompanionSettingsStore(settingsFile));
            using var again = new MainViewModel(second, new ImmediateDispatcher(), lanAddresses: () => Array.Empty<string>());
            await again.StartAsync();

            Assert.Equal(code, again.PairingCode);
            Assert.Empty(again.Events);
        }
        finally
        {
            foreach (var path in new[] { sessionFile, settingsFile })
            {
                var folder = Path.GetDirectoryName(path);
                if (folder is not null && Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }
    }
}
