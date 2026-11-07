using Presenta.Domain.Keyboard;
using Presenta.Targeting;
using Presenta.TestSupport;
using static Presenta.TestSupport.FakeWindowSystem;

namespace Presenta.Targeting.Tests;

public class CandidateListTests
{
    [Fact]
    public void Own_invisible_cloaked_tool_and_untitled_windows_are_never_offered()
    {
        var windows = new FakeWindowSystem { CurrentProcessId = 1000 }
            .Add(Window(1, "Good window", "notepad"))
            .Add(Window(2, "Companion", "Presenta", processId: 1000))
            .Add(Window(3, "Hidden", "app") with { IsVisible = false })
            .Add(Window(4, "Cloaked", "app") with { IsCloaked = true })
            .Add(Window(5, "Tool", "app") with { IsToolWindow = true })
            .Add(Window(6, string.Empty, "app"))
            .Add(Window(7, "   ", "app"));
        var service = new PresentationTargetService(windows, new InMemoryTargetSettingsStore());

        var candidates = service.ListCandidates();

        Assert.Equal(new long[] { 1 }, candidates.Select(c => c.Window.Handle));
    }

    [Fact]
    public void A_powerpoint_slide_show_is_suggested_and_listed_first_but_the_editing_window_is_not()
    {
        var windows = new FakeWindowSystem()
            .Add(Window(1, "Notes", "notepad"))
            .Add(Window(2, "deck.pptx - PowerPoint", "POWERPNT", "PPTFrameClass"))
            .Add(Window(3, "Slide Show - deck.pptx", "POWERPNT", "screenClass"))
            .Add(Window(4, "Another", "chrome", "screenClass"));
        var service = new PresentationTargetService(windows, new InMemoryTargetSettingsStore());

        var candidates = service.ListCandidates();

        Assert.Equal(3, candidates[0].Window.Handle);
        Assert.True(candidates[0].IsSuggested);
        Assert.All(candidates.Skip(1), c => Assert.False(c.IsSuggested));
        Assert.Equal(4, candidates.Count);
    }

    [Fact]
    public void A_minimized_window_is_still_a_candidate()
    {
        var windows = new FakeWindowSystem().Add(Window(1, "Show", minimized: true));
        var service = new PresentationTargetService(windows, new InMemoryTargetSettingsStore());

        Assert.Single(service.ListCandidates());
    }

    [Fact]
    public void Selecting_a_window_that_is_not_a_candidate_chooses_nothing()
    {
        var windows = new FakeWindowSystem { CurrentProcessId = 1000 };
        var own = Window(1, "Companion", processId: 1000);
        windows.Add(own);
        var service = new PresentationTargetService(windows, new InMemoryTargetSettingsStore());

        Assert.False(service.Select(own));
        Assert.Null(service.Identity);
    }
}

public class TargetResolutionTests
{
    private static (PresentationTargetService Service, FakeWindowSystem Windows, InMemoryTargetSettingsStore Store) Create(params WindowInfo[] windows)
    {
        var system = new FakeWindowSystem();
        foreach (var window in windows)
        {
            system.Add(window);
        }

        var store = new InMemoryTargetSettingsStore();
        return (new PresentationTargetService(system, store), system, store);
    }

    [Fact]
    public void Nothing_selected_resolves_to_none_selected()
    {
        var (service, _, _) = Create(Window(1, "Show"));

        Assert.Equal(TargetState.NoneSelected, service.Resolve().State);
    }

    [Fact]
    public void A_selected_live_window_is_ready()
    {
        var show = Window(1, "Show - deck", "POWERPNT", "screenClass");
        var (service, _, _) = Create(show);

        Assert.True(service.Select(show));

        var resolution = service.Resolve();
        Assert.Equal(TargetState.Ready, resolution.State);
        Assert.Equal(1, resolution.Window!.Handle);
    }

    [Fact]
    public void A_dead_handle_is_re_resolved_to_the_new_window_with_the_same_identity()
    {
        var first = Window(1, "Show - deck", "POWERPNT", "screenClass");
        var (service, windows, _) = Create(first);
        service.Select(first);
        windows.Remove(1);
        windows.Add(Window(2, "Show - deck", "POWERPNT", "screenClass"));

        var resolution = service.Resolve();

        Assert.Equal(TargetState.Ready, resolution.State);
        Assert.Equal(2, resolution.Window!.Handle);
    }

    [Fact]
    public void A_closed_window_with_no_replacement_is_not_found()
    {
        var show = Window(1, "Show - deck", "POWERPNT", "screenClass");
        var (service, windows, _) = Create(show);
        service.Select(show);
        windows.Remove(1);

        Assert.Equal(TargetState.NotFound, service.Resolve().State);
    }

    [Fact]
    public void A_handle_reused_by_a_different_program_is_not_trusted()
    {
        var show = Window(1, "Show - deck", "POWERPNT", "screenClass");
        var (service, windows, _) = Create(show);
        service.Select(show);
        windows.Remove(1);
        windows.Add(Window(1, "Something else", "notepad", "Notepad"));

        Assert.Equal(TargetState.NotFound, service.Resolve().State);
    }

    [Fact]
    public void Two_identical_windows_after_a_restart_are_ambiguous_and_no_choice_is_guessed()
    {
        var identity = new PresentationTargetIdentity("notepad", "Notepad", "Untitled");
        var system = new FakeWindowSystem()
            .Add(Window(1, "Untitled - Notepad", "notepad", "Notepad"))
            .Add(Window(2, "Untitled - Notepad", "notepad", "Notepad"));
        var service = new PresentationTargetService(system, new InMemoryTargetSettingsStore(identity));

        Assert.Equal(TargetState.Ambiguous, service.Resolve().State);
    }

    [Fact]
    public void The_remembered_handle_breaks_a_tie_between_identical_windows()
    {
        var one = Window(1, "Untitled - Notepad", "notepad", "Notepad");
        var two = Window(2, "Untitled - Notepad", "notepad", "Notepad");
        var (service, _, _) = Create(one, two);
        service.Select(two);

        var resolution = service.Resolve();

        Assert.Equal(TargetState.Ready, resolution.State);
        Assert.Equal(2, resolution.Window!.Handle);
    }

    [Fact]
    public void The_title_match_survives_the_modified_marker_and_case()
    {
        var note = Window(1, "notes.txt - Notepad", "notepad", "Notepad");
        var (service, windows, _) = Create(note);
        service.Select(note);

        windows.Update(1, w => w with { Title = "*Notes.TXT - Notepad" });

        Assert.Equal(TargetState.Ready, service.Resolve().State);
    }

    [Fact]
    public void A_window_whose_title_changed_beyond_the_stored_part_is_not_found()
    {
        var page = Window(1, "Quarterly slides - Browser", "chrome", "Chrome_WidgetWin_1");
        var (service, windows, _) = Create(page);
        service.Select(page);

        windows.Update(1, w => w with { Title = "Inbox - Browser" });

        Assert.Equal(TargetState.NotFound, service.Resolve().State);
    }

    [Fact]
    public void A_minimized_target_reports_minimized()
    {
        var show = Window(1, "Show", minimized: true);
        var (service, _, _) = Create(show);
        service.Select(show);

        Assert.Equal(TargetState.Minimized, service.Resolve().State);
    }

    [Fact]
    public void An_elevated_target_is_reported_when_the_companion_is_not_elevated()
    {
        var show = Window(1, "Show", elevated: true);
        var (service, windows, _) = Create(show);
        service.Select(show);

        Assert.Equal(TargetState.Elevated, service.Resolve().State);

        windows.IsCurrentProcessElevated = true;
        Assert.Equal(TargetState.Ready, service.Resolve().State);
    }

    [Fact]
    public void Clearing_the_choice_forgets_it_and_saves_that()
    {
        var show = Window(1, "Show");
        var (service, _, store) = Create(show);
        service.Select(show);

        service.Clear();

        Assert.Equal(TargetState.NoneSelected, service.Resolve().State);
        Assert.Null(store.Identity);
    }

    [Fact]
    public void Selecting_raises_the_selection_changed_event_and_saves_the_identity()
    {
        var show = Window(1, "Show - deck", "POWERPNT", "screenClass");
        var (service, _, store) = Create(show);
        var raised = 0;
        service.SelectionChanged += (_, _) => raised++;

        service.Select(show);

        Assert.Equal(1, raised);
        Assert.Equal(new PresentationTargetIdentity("POWERPNT", "screenClass", "Show - deck"), store.Identity);
    }

    [Fact]
    public void The_saved_choice_is_restored_by_a_new_service()
    {
        var show = Window(1, "Show - deck", "POWERPNT", "screenClass");
        var (first, windows, store) = Create(show);
        first.Select(show);

        var second = new PresentationTargetService(windows, store);

        Assert.Equal(TargetState.Ready, second.Resolve().State);
    }
}

public class ActivationTests
{
    private static readonly WindowInfo Show = Window(10, "Show - deck", "POWERPNT", "screenClass");
    private static readonly WindowInfo Other = Window(20, "Studio", "studio", "Frame");

    private static (WindowSlideKeyboard Keyboard, PresentationTargetService Service, FakeWindowSystem Windows) Create(params WindowInfo[] windows)
    {
        var system = new FakeWindowSystem();
        foreach (var window in windows)
        {
            system.Add(window);
        }

        var service = new PresentationTargetService(system, new InMemoryTargetSettingsStore());
        service.Select(Show);
        return (new WindowSlideKeyboard(service, system), service, system);
    }

    [Fact]
    public void A_background_target_is_activated_confirmed_and_only_then_receives_the_key()
    {
        var (keyboard, _, windows) = Create(Show, Other);
        windows.Foreground = 20;

        var result = keyboard.Send(SlideDirection.Next);

        Assert.Equal(KeyDeliveryResult.Delivered, result);
        Assert.Equal(new[] { "activate 10", "wait 10", "key Next" }, windows.Calls);
        Assert.Equal(new[] { (10L, SlideDirection.Next) }, windows.Keys);
    }

    [Fact]
    public void The_previous_direction_presses_previous_into_the_target()
    {
        var (keyboard, _, windows) = Create(Show, Other);
        windows.Foreground = 20;

        Assert.Equal(KeyDeliveryResult.Delivered, keyboard.Send(SlideDirection.Previous));
        Assert.Equal(new[] { (10L, SlideDirection.Previous) }, windows.Keys);
    }

    [Fact]
    public void An_already_foreground_target_skips_activation()
    {
        var (keyboard, _, windows) = Create(Show, Other);
        windows.Foreground = 10;

        Assert.Equal(KeyDeliveryResult.Delivered, keyboard.Send(SlideDirection.Next));

        Assert.DoesNotContain(windows.Calls, c => c.StartsWith("activate", StringComparison.Ordinal));
        Assert.DoesNotContain(windows.Calls, c => c.StartsWith("restore", StringComparison.Ordinal));
        Assert.Single(windows.Keys);
    }

    [Fact]
    public void A_minimized_target_is_restored_before_activation_and_other_windows_are_untouched()
    {
        var minimized = Show with { IsMinimized = true };
        var (keyboard, _, windows) = Create(minimized, Other);
        windows.Foreground = 20;

        Assert.Equal(KeyDeliveryResult.Delivered, keyboard.Send(SlideDirection.Next));

        var calls = windows.Calls.ToList();
        Assert.True(calls.IndexOf("restore 10") < calls.IndexOf("activate 10"));
        Assert.DoesNotContain("restore 20", calls);
        Assert.Equal(10, windows.Keys.Single().Window);
    }

    [Fact]
    public void When_windows_refuses_to_activate_no_key_is_sent()
    {
        var (keyboard, _, windows) = Create(Show, Other);
        windows.Foreground = 20;
        windows.Behavior = ForegroundBehavior.Refuse;

        Assert.Equal(KeyDeliveryResult.ActivationFailed, keyboard.Send(SlideDirection.Next));

        Assert.Empty(windows.Keys);
        Assert.DoesNotContain(windows.Calls, c => c.StartsWith("key", StringComparison.Ordinal));
    }

    [Fact]
    public void When_the_foreground_cannot_be_confirmed_no_key_is_sent_and_the_thief_gets_nothing()
    {
        var (keyboard, _, windows) = Create(Show, Other);
        windows.Foreground = 20;
        windows.Behavior = ForegroundBehavior.AcceptButStayBehind;

        Assert.Equal(KeyDeliveryResult.NotConfirmedForeground, keyboard.Send(SlideDirection.Next));

        Assert.Empty(windows.Keys);
        Assert.Equal(20, windows.Foreground);
    }

    [Fact]
    public void The_attempts_are_bounded()
    {
        var (keyboard, _, windows) = Create(Show, Other);
        windows.Foreground = 20;
        windows.Behavior = ForegroundBehavior.AcceptButStayBehind;

        keyboard.Send(SlideDirection.Next);

        Assert.Equal(3, windows.Calls.Count(c => c == "activate 10"));
    }

    [Fact]
    public void A_foreground_that_is_lost_between_the_confirmation_and_the_key_aborts_without_a_key()
    {
        var (keyboard, _, windows) = Create(Show, Other);
        windows.Foreground = 20;
        windows.AfterConfirmation = w => w.Foreground = 20; // the user clicks elsewhere at the worst moment

        Assert.Equal(KeyDeliveryResult.NotConfirmedForeground, keyboard.Send(SlideDirection.Next));
        Assert.Empty(windows.Keys);
    }

    [Fact]
    public void A_window_that_steals_the_foreground_back_during_activation_gets_no_key()
    {
        var (keyboard, _, windows) = Create(Show, Other);
        windows.Foreground = 20;
        windows.StealForegroundAfterActivation = 20;

        Assert.Equal(KeyDeliveryResult.NotConfirmedForeground, keyboard.Send(SlideDirection.Next));
        Assert.Empty(windows.Keys);
    }

    [Fact]
    public void A_missing_target_sends_nothing()
    {
        var (keyboard, _, windows) = Create(Show, Other);
        windows.Remove(10);

        Assert.Equal(KeyDeliveryResult.TargetNotFound, keyboard.Send(SlideDirection.Next));
        Assert.Empty(windows.Keys);
        Assert.DoesNotContain(windows.Calls, c => c.StartsWith("activate", StringComparison.Ordinal));
    }

    [Fact]
    public void No_selection_sends_nothing()
    {
        var system = new FakeWindowSystem().Add(Show).Add(Other);
        system.Foreground = 20;
        var keyboard = new WindowSlideKeyboard(new PresentationTargetService(system, new InMemoryTargetSettingsStore()), system);

        Assert.Equal(KeyDeliveryResult.NoTargetSelected, keyboard.Send(SlideDirection.Next));
        Assert.Empty(system.Keys);
    }

    [Fact]
    public void An_elevated_target_sends_nothing_and_does_not_even_try_to_activate()
    {
        var elevated = Show with { IsElevated = true };
        var (keyboard, _, windows) = Create(elevated, Other);
        windows.Foreground = 20;

        Assert.Equal(KeyDeliveryResult.TargetElevated, keyboard.Send(SlideDirection.Next));

        Assert.Empty(windows.Keys);
        Assert.DoesNotContain(windows.Calls, c => c.StartsWith("activate", StringComparison.Ordinal));
    }

    [Fact]
    public void An_ambiguous_target_sends_nothing()
    {
        var system = new FakeWindowSystem().Add(Show).Add(Show with { Handle = 11 }).Add(Other);
        var store = new InMemoryTargetSettingsStore(PresentationTargetIdentity.From(Show));
        var keyboard = new WindowSlideKeyboard(new PresentationTargetService(system, store), system);
        system.Foreground = 20;

        Assert.Equal(KeyDeliveryResult.TargetAmbiguous, keyboard.Send(SlideDirection.Next));
        Assert.Empty(system.Keys);
    }

    [Fact]
    public void A_rejected_key_event_is_reported_as_a_send_failure()
    {
        var (keyboard, _, windows) = Create(Show, Other);
        windows.Foreground = 20;
        windows.SendArrowResult = false;

        Assert.Equal(KeyDeliveryResult.SendFailed, keyboard.Send(SlideDirection.Next));
    }

    [Fact]
    public void Focus_is_not_given_back_to_the_previous_window()
    {
        var (keyboard, _, windows) = Create(Show, Other);
        windows.Foreground = 20;

        keyboard.Send(SlideDirection.Next);

        Assert.Equal(10, windows.Foreground);
        Assert.DoesNotContain(windows.Calls, c => c == "activate 20");
    }

    [Fact]
    public void Verify_activates_and_confirms_without_pressing_any_key()
    {
        var (_, service, windows) = Create(Show, Other);
        windows.Foreground = 20;

        var result = service.Activate();

        Assert.True(result.IsInForeground);
        Assert.Equal(10, windows.Foreground);
        Assert.Empty(windows.Keys);
        Assert.DoesNotContain(windows.Calls, c => c.StartsWith("key", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_directions_are_rejected_before_anything_happens()
    {
        var (keyboard, _, windows) = Create(Show, Other);
        windows.Foreground = 20;

        Assert.Throws<ArgumentOutOfRangeException>(() => keyboard.Send((SlideDirection)9));
        Assert.Empty(windows.Calls);
    }

    [Fact]
    public void Whatever_the_path_only_right_and_left_arrows_can_ever_be_requested_from_the_window_system()
    {
        var scenarios = new Action<FakeWindowSystem>[]
        {
            w => w.Behavior = ForegroundBehavior.Succeed,
            w => w.Behavior = ForegroundBehavior.Refuse,
            w => w.Behavior = ForegroundBehavior.AcceptButStayBehind,
            w => w.SendArrowResult = false,
            w => w.Foreground = 10,
        };

        foreach (var scenario in scenarios)
        {
            var (keyboard, _, windows) = Create(Show, Other);
            windows.Foreground = 20;
            scenario(windows);
            foreach (var direction in Enum.GetValues<SlideDirection>())
            {
                keyboard.Send(direction);
            }

            var keyCalls = windows.Calls.Where(c => c.StartsWith("key", StringComparison.Ordinal)).Distinct().Order().ToArray();
            Assert.All(keyCalls, call => Assert.Contains(call, new[] { "key Next", "key Previous" }));
        }
    }
}
