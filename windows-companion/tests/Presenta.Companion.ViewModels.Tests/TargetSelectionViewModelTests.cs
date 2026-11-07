using Presenta.Server;
using Presenta.Targeting;
using Presenta.TestSupport;
using static Presenta.TestSupport.FakeWindowSystem;

namespace Presenta.Companion.ViewModels.Tests;

public class TargetSelectionViewModelTests
{
    private static readonly WindowInfo Show = Window(10, "Slide Show - deck.pptx", "POWERPNT", "screenClass");
    private static readonly WindowInfo Notes = Window(11, "notes.txt - Notepad", "notepad", "Notepad");
    private static readonly WindowInfo Studio = Window(20, "Studio", "studio", "Frame");

    private static (TargetSelectionViewModel Vm, PresentationTargetService Service, FakeWindowSystem Windows, InMemoryTargetSettingsStore Store) Create(
        params WindowInfo[] windows)
    {
        var system = new FakeWindowSystem();
        foreach (var window in windows)
        {
            system.Add(window);
        }

        var store = new InMemoryTargetSettingsStore();
        var service = new PresentationTargetService(system, store);
        return (new TargetSelectionViewModel(service, new ImmediateDispatcher()), service, system, store);
    }

    [Fact]
    public void The_list_shows_candidates_with_the_suggested_slide_show_first_and_labelled()
    {
        var (vm, _, _, _) = Create(Notes, Show, Studio);

        Assert.Equal(3, vm.Items.Count);
        Assert.True(vm.Items[0].IsSuggested);
        Assert.Equal(10, vm.Items[0].Window.Handle);
        Assert.Equal(UiText.Format("Target_ItemSuggested", Show.Title, Show.ProcessName), vm.Items[0].Display);
        Assert.Equal(UiText.Format("Target_Item", Notes.Title, Notes.ProcessName), vm.Items[1].Display);
        Assert.True(vm.HasItems);
    }

    [Fact]
    public void Nothing_chosen_shows_the_none_status_and_no_selection()
    {
        var (vm, _, _, _) = Create(Show);

        Assert.Null(vm.SelectedItem);
        Assert.Equal(UiText.Get("Target_Status_None"), vm.StatusText);
        Assert.Equal(TargetSeverity.Neutral, vm.Severity);
    }

    [Fact]
    public void Choosing_a_row_saves_the_choice_and_reports_it_ready()
    {
        var (vm, service, _, store) = Create(Show, Notes);

        vm.SelectedItem = vm.Items[0];

        Assert.Equal(PresentationTargetIdentity.From(Show), service.Identity);
        Assert.Equal(service.Identity, store.Identity);
        Assert.Equal(UiText.Get("Target_Status_Ready"), vm.StatusText);
        Assert.True(vm.IsGood);
        Assert.False(vm.IsWarning);
    }

    [Fact]
    public void Refresh_keeps_the_chosen_window_selected_and_picks_up_new_windows()
    {
        var (vm, _, windows, _) = Create(Show);
        vm.SelectedItem = vm.Items[0];

        windows.Add(Notes);
        vm.RefreshCommand.Execute(null);

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(10, vm.SelectedItem!.Window.Handle);
    }

    [Fact]
    public void Refresh_never_changes_what_is_saved()
    {
        var (vm, service, windows, store) = Create(Show, Notes);
        vm.SelectedItem = vm.Items[0];
        var saved = store.Identity;

        windows.Remove(10);
        vm.RefreshList();

        Assert.Null(vm.SelectedItem);
        Assert.Equal(saved, service.Identity);
        Assert.Equal(saved, store.Identity);
    }

    [Fact]
    public void An_empty_list_is_reported()
    {
        var (vm, _, _, _) = Create();

        Assert.True(vm.HasNoItems);
        Assert.False(vm.HasItems);
    }

    [Theory]
    [InlineData("closed", "Target_Status_NotFound", TargetSeverity.Warning)]
    [InlineData("minimized", "Target_Status_Minimized", TargetSeverity.Good)]
    [InlineData("elevated", "Target_Status_Elevated", TargetSeverity.Warning)]
    [InlineData("duplicate", "Target_Status_Ambiguous", TargetSeverity.Warning)]
    public void The_status_follows_what_happens_to_the_window(string change, string expectedKey, TargetSeverity expectedSeverity)
    {
        var (vm, _, windows, _) = Create(Show);
        vm.SelectedItem = vm.Items[0];
        switch (change)
        {
            case "closed":
                windows.Remove(10);
                break;
            case "minimized":
                windows.Update(10, w => w with { IsMinimized = true });
                break;
            case "elevated":
                windows.Update(10, w => w with { IsElevated = true });
                break;
            default:
                // The remembered handle dies and two look-alikes appear.
                windows.Remove(10);
                windows.Add(Show with { Handle = 30 }).Add(Show with { Handle = 31 });
                break;
        }

        vm.RefreshStatus();

        Assert.Equal(UiText.Get(expectedKey), vm.StatusText);
        Assert.Equal(expectedSeverity, vm.Severity);
    }

    [Fact]
    public async Task Verify_brings_the_window_to_the_front_reports_it_and_presses_no_key()
    {
        var (vm, _, windows, _) = Create(Show, Studio);
        vm.SelectedItem = vm.Items[0];
        windows.Foreground = 20;

        vm.VerifyCommand.Execute(null);
        await vm.LastOperation!;

        Assert.Equal(10, windows.Foreground);
        Assert.Equal(UiText.Get("Target_Verify_Ok"), vm.StatusText);
        Assert.True(vm.IsGood);
        Assert.Empty(windows.Keys);
        Assert.DoesNotContain(windows.Calls, c => c.StartsWith("key", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ForegroundBehavior.Refuse, "Target_Verify_Refused")]
    [InlineData(ForegroundBehavior.AcceptButStayBehind, "Target_Verify_NotConfirmed")]
    public async Task Verify_says_so_when_windows_does_not_bring_the_window_to_the_front(ForegroundBehavior behavior, string expectedKey)
    {
        var (vm, _, windows, _) = Create(Show, Studio);
        vm.SelectedItem = vm.Items[0];
        windows.Foreground = 20;
        windows.Behavior = behavior;

        vm.VerifyCommand.Execute(null);
        await vm.LastOperation!;

        Assert.Equal(UiText.Get(expectedKey), vm.StatusText);
        Assert.True(vm.IsWarning);
        Assert.Empty(windows.Keys);
    }

    [Fact]
    public async Task Verify_with_nothing_chosen_says_none_chosen()
    {
        var (vm, _, windows, _) = Create(Show);

        vm.VerifyCommand.Execute(null);
        await vm.LastOperation!;

        Assert.Equal(UiText.Get("Target_Status_None"), vm.StatusText);
        Assert.DoesNotContain(windows.Calls, c => c.StartsWith("activate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_timer_tick_keeps_the_verification_message_readable_for_a_few_ticks()
    {
        var (vm, _, windows, _) = Create(Show, Studio);
        vm.SelectedItem = vm.Items[0];
        windows.Foreground = 20;
        vm.VerifyCommand.Execute(null);
        await vm.LastOperation!;

        vm.Tick();
        vm.Tick();
        vm.Tick();
        Assert.Equal(UiText.Get("Target_Verify_Ok"), vm.StatusText);

        vm.Tick();
        Assert.Equal(UiText.Get("Target_Status_Ready"), vm.StatusText);
    }

    [Fact]
    public void The_keys_off_note_follows_the_switch()
    {
        var (vm, _, _, _) = Create(Show);
        Assert.False(vm.KeysOffNoteVisible);

        vm.KeysEnabled = false;

        Assert.True(vm.KeysOffNoteVisible);
    }

    [Fact]
    public void The_main_view_model_exposes_the_section_and_mirrors_the_keys_switch()
    {
        var keyboard = new RecordingSlideKeyboard();
        var host = new CompanionHost(
            new CompanionServerOptions { Port = 0, PairingCode = "123456" },
            keyboard,
            new InMemoryHistoryStore(),
            new ManualTimeProvider());
        var (target, _, _, _) = Create(Show);

        using var main = new MainViewModel(host, new ImmediateDispatcher(), lanAddresses: () => Array.Empty<string>(), target: target);
        using var withoutTarget = new MainViewModel(host, new ImmediateDispatcher(), lanAddresses: () => Array.Empty<string>());

        Assert.True(main.HasTarget);
        Assert.False(withoutTarget.HasTarget);
        Assert.False(target.KeysOffNoteVisible);

        main.SendKeys = false;
        Assert.True(target.KeysOffNoteVisible);
    }

    [Fact]
    public void Every_new_text_comes_from_the_resource_file_and_is_Spanish()
    {
        var keys = new[]
        {
            "Target_Title", "Target_Hint", "Target_Refresh", "Target_Verify", "Target_Empty", "Target_KeysOff", "Target_Item",
            "Target_ItemSuggested", "Target_Status_None", "Target_Status_Ready", "Target_Status_Minimized", "Target_Status_NotFound",
            "Target_Status_Ambiguous", "Target_Status_Elevated", "Target_Verify_Ok", "Target_Verify_Refused",
            "Target_Verify_NotConfirmed", "Event_StartRejectedNoTarget",
        };

        foreach (var key in keys)
        {
            var text = UiText.Get(key);
            Assert.False(string.IsNullOrWhiteSpace(text), key);
        }

        // The text is Spanish and lives only in the resource file: none of the keys falls back to its own name.
        Assert.All(keys, key => Assert.NotEqual(key, UiText.Get(key)));
    }

    [Fact]
    public void The_labels_class_reads_the_section_captions_from_the_resource_file()
    {
        var labels = new UiLabels();

        Assert.Equal(UiText.Get("Target_Title"), labels.TargetTitle);
        Assert.Equal(UiText.Get("Target_Refresh"), labels.TargetRefresh);
        Assert.Equal(UiText.Get("Target_Verify"), labels.TargetVerify);
        Assert.Equal(UiText.Get("Target_Hint"), labels.TargetHint);
        Assert.Equal(UiText.Get("Target_Empty"), labels.TargetEmpty);
        Assert.Equal(UiText.Get("Target_KeysOff"), labels.TargetKeysOff);
    }
}
