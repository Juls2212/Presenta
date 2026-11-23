using Presenta.Domain.Keyboard;
using Presenta.Targeting;
using Presenta.TestSupport;
using static Presenta.TestSupport.FakeWindowSystem;

namespace Presenta.Targeting.Tests;

/// <summary>
/// The rule for "the chosen window is in front": the chosen window itself, or a window of the same program that it owns
/// (a slide-show window owned by the main frame). Everything else is refused, and the key never reaches a thief.
/// </summary>
public class ForegroundRuleTests
{
    private const int PowerPointPid = 3000;

    private static readonly WindowInfo Frame = Window(1, "deck.pptx - PowerPoint", "POWERPNT", "PPTFrameClass", PowerPointPid);
    private static readonly WindowInfo Show = Window(2, "PowerPoint Slide Show - [deck.pptx]", "POWERPNT", "screenClass", PowerPointPid);
    private static readonly WindowInfo Other = Window(3, "Another PowerPoint window", "POWERPNT", "OtherClass", PowerPointPid);
    private static readonly WindowInfo Thief = Window(FakeWindowSystem.ThiefHandle, "Android Studio", "studio", "Frame", 4000);

    private static (PresentationTargetService Service, FakeWindowSystem Windows) Create(WindowInfo chosen, params WindowInfo[] others)
    {
        var windows = new FakeWindowSystem().Add(Frame).Add(Show).Add(Other).Add(Thief);
        foreach (var extra in others)
        {
            windows.Add(extra);
        }

        windows.Owners[Show.Handle] = Frame.Handle; // the show window is owned by the main frame
        windows.Foreground = Thief.Handle;
        var service = new PresentationTargetService(windows, new InMemoryTargetSettingsStore());
        Assert.True(service.Select(chosen));
        return (service, windows);
    }

    [Fact]
    public void An_owned_window_in_front_is_accepted_and_the_key_goes_to_it()
    {
        var (service, windows) = Create(Frame);
        windows.StealForegroundAfterActivation = Show.Handle;

        var result = new WindowSlideKeyboard(service, windows).Send(SlideDirection.Next);

        Assert.Equal(KeyDeliveryResult.Delivered, result);
        Assert.Equal(new[] { (Show.Handle, SlideDirection.Next) }, windows.Keys);
    }

    [Theory]
    [InlineData(ForegroundRelation.Target)]
    [InlineData(ForegroundRelation.OwnedByTarget)]
    [InlineData(ForegroundRelation.OwnerOfTarget)]
    [InlineData(ForegroundRelation.SameProcessOther)]
    [InlineData(ForegroundRelation.OtherProcess)]
    [InlineData(ForegroundRelation.Unknown)]
    public void Each_relation_is_classified_and_only_the_target_and_its_owned_windows_are_accepted(ForegroundRelation expected)
    {
        var (service, windows) = Create(Frame);
        windows.Add(Window(5, "Owned of owned", "POWERPNT", "screenClass", PowerPointPid));
        windows.Owners[5] = Show.Handle; // owned by the show window, which is owned by the frame
        var foreground = expected switch
        {
            ForegroundRelation.Target => Frame.Handle,
            ForegroundRelation.OwnedByTarget => 5L,
            ForegroundRelation.OwnerOfTarget => Frame.Handle,
            ForegroundRelation.SameProcessOther => Other.Handle,
            ForegroundRelation.OtherProcess => Thief.Handle,
            _ => 0L,
        };
        // For the owner-of-target case the roles are swapped: the show window is chosen and its owner (the frame) is in front.
        var target = expected == ForegroundRelation.OwnerOfTarget ? Show : Frame;

        var relation = service.GetForegroundRelation(target, foreground);

        Assert.Equal(expected, relation);
    }

    [Fact]
    public void The_owner_of_the_chosen_window_in_front_is_not_accepted()
    {
        var (service, windows) = Create(Show); // the show window is chosen, as the suggestion says
        windows.StealForegroundAfterActivation = Frame.Handle; // the program leaves its frame in front

        var result = new WindowSlideKeyboard(service, windows).Send(SlideDirection.Next);

        Assert.Equal(KeyDeliveryResult.NotConfirmedForeground, result);
        Assert.Empty(windows.Keys);
    }

    [Fact]
    public void An_unrelated_window_of_the_same_program_in_front_is_not_accepted()
    {
        var (service, windows) = Create(Frame);
        windows.StealForegroundAfterActivation = Other.Handle;

        var result = new WindowSlideKeyboard(service, windows).Send(SlideDirection.Next);

        Assert.Equal(KeyDeliveryResult.NotConfirmedForeground, result);
        Assert.Empty(windows.Keys);
    }

    [Fact]
    public void A_window_of_another_program_is_never_accepted_even_when_it_claims_to_be_owned_by_the_chosen_window()
    {
        var (service, windows) = Create(Frame);
        windows.Owners[Thief.Handle] = Frame.Handle; // an owner link across programs must not count
        windows.StealForegroundAfterActivation = Thief.Handle;

        var result = new WindowSlideKeyboard(service, windows).Send(SlideDirection.Next);

        Assert.Equal(KeyDeliveryResult.NotConfirmedForeground, result);
        Assert.Empty(windows.Keys);
    }

    [Fact]
    public void An_owner_chain_that_leaves_the_program_does_not_reach_the_chosen_window()
    {
        var (service, windows) = Create(Frame);
        var middle = Window(6, "Owned by a thief", "POWERPNT", "screenClass", PowerPointPid);
        windows.Add(middle);
        windows.Owners[middle.Handle] = Thief.Handle; // owned by another program's window
        windows.Owners[Thief.Handle] = Frame.Handle;

        Assert.Equal(ForegroundRelation.SameProcessOther, service.GetForegroundRelation(Frame, middle.Handle));
    }

    [Fact]
    public void A_thief_that_takes_the_foreground_after_the_confirmation_stops_the_key()
    {
        var (service, windows) = Create(Frame);
        windows.AfterConfirmation = w => w.Foreground = Thief.Handle;

        var result = new WindowSlideKeyboard(service, windows).Send(SlideDirection.Next);

        Assert.Equal(KeyDeliveryResult.NotConfirmedForeground, result);
        Assert.Empty(windows.Keys);
    }

    [Fact]
    public void An_owned_window_that_takes_the_foreground_after_the_confirmation_still_gets_the_key()
    {
        var (service, windows) = Create(Frame);
        windows.AfterConfirmation = w => w.Foreground = Show.Handle;

        var result = new WindowSlideKeyboard(service, windows).Send(SlideDirection.Previous);

        Assert.Equal(KeyDeliveryResult.Delivered, result);
        Assert.Equal(new[] { (Show.Handle, SlideDirection.Previous) }, windows.Keys);
    }

    // ---- hypothesis C: the identity keeps the class, and resolution never falls back to another class ----

    [Fact]
    public void The_saved_identity_keeps_the_window_class_and_the_editing_window_never_matches_a_slide_show_choice()
    {
        var windows = new FakeWindowSystem().Add(Show).Add(Frame);
        var service = new PresentationTargetService(windows, new InMemoryTargetSettingsStore());
        service.Select(Show);
        Assert.Equal("screenClass", service.Identity!.WindowClass);

        windows.Remove(Show.Handle); // the show ends; only the editing window of the same file is left

        Assert.Equal(TargetState.NotFound, service.GetState());
        Assert.Equal(ActivationOutcome.TargetNotFound, service.Activate().Outcome);
    }

    [Fact]
    public void A_frame_choice_does_not_match_the_show_window_with_a_similar_title()
    {
        var windows = new FakeWindowSystem().Add(Show).Add(Frame with { Title = "PowerPoint Slide Show - [deck.pptx]" });
        var service = new PresentationTargetService(windows, new InMemoryTargetSettingsStore());
        service.Select(windows.GetWindow(Frame.Handle)!);

        windows.Remove(Frame.Handle);

        Assert.Equal(TargetState.NotFound, service.GetState()); // the show window has the same title but another class
    }
}

public class DiagnosticReportTests
{
    private const int PowerPointPid = 3000;

    private static (PresentationTargetService Service, FakeWindowSystem Windows) Create()
    {
        var windows = new FakeWindowSystem { CurrentProcessId = 1000 }
            .Add(Window(1, "deck.pptx - PowerPoint", "POWERPNT", "PPTFrameClass", PowerPointPid))
            .Add(Window(2, "PowerPoint Slide Show - [deck.pptx]", "POWERPNT", "screenClass", PowerPointPid))
            .Add(Window(FakeWindowSystem.ThiefHandle, "Android Studio", "studio", "Frame", 4000));
        windows.Owners[2] = 1;
        windows.Foreground = FakeWindowSystem.ThiefHandle;
        var service = new PresentationTargetService(windows, new InMemoryTargetSettingsStore());
        service.Select(windows.GetWindow(1)!);
        return (service, windows);
    }

    [Fact]
    public void The_report_covers_the_system_the_target_the_foreground_every_step_and_the_decision_and_sends_no_key()
    {
        var (service, windows) = Create();
        windows.StealForegroundAfterActivation = 2; // the program hands the focus to its owned show window

        var report = service.CreateDiagnosticReport(new FixedTime());

        Assert.StartsWith("Presenta window diagnostic report", report, StringComparison.Ordinal);
        Assert.Contains("window titles can contain file names", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No key was sent", report, StringComparison.Ordinal);
        Assert.Contains("Windows:", report, StringComparison.Ordinal);
        Assert.Contains("Companion process: pid 1000, elevated: no", report, StringComparison.Ordinal);
        Assert.Contains("class 'PPTFrameClass'", report, StringComparison.Ordinal);
        Assert.Contains("handle 0x1,", report, StringComparison.Ordinal);
        Assert.Contains("pid 3000, process 'POWERPNT'", report, StringComparison.Ordinal);
        Assert.Contains("top-most no", report, StringComparison.Ordinal);
        Assert.Contains("== Foreground before activation ==", report, StringComparison.Ordinal);
        Assert.Contains("Android Studio", report, StringComparison.Ordinal);
        Assert.Contains("a window of another program", report, StringComparison.Ordinal);
        Assert.Contains("All top-level windows of process 3000", report, StringComparison.Ordinal);
        Assert.Contains("class 'screenClass'", report, StringComparison.Ordinal);
        Assert.Contains("AttachThreadInput(companion, foreground, attach): succeeded", report, StringComparison.Ordinal);
        Assert.Contains("SetForegroundWindow: succeeded", report, StringComparison.Ordinal);
        Assert.Contains("BringWindowToTop: succeeded", report, StringComparison.Ordinal);
        Assert.Contains("AttachThreadInput(detach): succeeded", report, StringComparison.Ordinal);
        Assert.Contains("it is a window owned by the target (accepted)", report, StringComparison.Ordinal);
        Assert.Contains("A key WOULD be sent", report, StringComparison.Ordinal);
        Assert.Empty(windows.Keys);
        Assert.DoesNotContain(windows.Calls, c => c.StartsWith("key", StringComparison.Ordinal));
    }

    [Fact]
    public void A_refused_activation_shows_the_win32_error_and_the_decision_not_to_send()
    {
        var (service, windows) = Create();
        windows.Behavior = ForegroundBehavior.Refuse;

        var report = service.CreateDiagnosticReport(new FixedTime());

        Assert.Contains("SetForegroundWindow: failed, Win32 error 5", report, StringComparison.Ordinal);
        Assert.Contains("Activation outcome: ActivationFailed", report, StringComparison.Ordinal);
        Assert.Contains("NO key would be sent: Windows refused", report, StringComparison.Ordinal);
        Assert.Empty(windows.Keys);
    }

    [Fact]
    public void A_target_that_never_gets_in_front_names_the_foreground_after_each_retry()
    {
        var (service, windows) = Create();
        windows.Behavior = ForegroundBehavior.AcceptButStayBehind;

        var report = service.CreateDiagnosticReport(new FixedTime());

        Assert.Contains("Attempt 3:", report, StringComparison.Ordinal);
        Assert.Contains("Accepted window in front within the confirmation time: no", report, StringComparison.Ordinal);
        Assert.Contains("NO key would be sent: the target was never confirmed in front", report, StringComparison.Ordinal);
        Assert.Empty(windows.Keys);
    }

    [Theory]
    [InlineData(false, "Nothing is selected.", "no target is selected")]
    [InlineData(true, "Resolution: NotFound", "the selected window cannot be found")]
    public void Without_a_usable_target_the_report_says_so(bool selectedThenClosed, string expectedLine, string expectedReason)
    {
        var (service, windows) = Create();
        if (selectedThenClosed)
        {
            windows.Remove(1);
        }
        else
        {
            service.Clear();
        }

        var report = service.CreateDiagnosticReport(new FixedTime());

        Assert.Contains(expectedLine, report, StringComparison.Ordinal);
        Assert.Contains(expectedReason, report, StringComparison.Ordinal);
        Assert.Contains("No activation attempt was made", report, StringComparison.Ordinal);
        Assert.Empty(windows.Keys);
    }

    [Fact]
    public void The_report_cannot_contain_a_pairing_code_because_it_only_describes_windows()
    {
        var (service, _) = Create();

        var report = service.CreateDiagnosticReport(new FixedTime());

        Assert.DoesNotContain("pairing", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123456", report, StringComparison.Ordinal);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    }
}
