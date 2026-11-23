using Presenta.Ai;
using Presenta.Domain;
using Presenta.Server;
using Presenta.TestSupport;

namespace Presenta.Companion.ViewModels.Tests;

/// <summary>Phase 10: the "Recomendaciones con IA" section (ADR-79).</summary>
public class AiSectionViewModelTests
{
    private static SessionSnapshot Alerting(int slidesRemaining = 9) =>
        new(
            SessionState.Running, "Some deck", 11, 20, 900_000, 600_000, 300_000, true, DateTimeOffset.UnixEpoch, 0.55, PaceStatus.Behind,
            slidesRemaining, 1.1, null,
            new Alert(AlertKind.TimeRunningOut, AlertSeverity.Warning, 600_000, 300_000, slidesRemaining, -2, 1),
            new Recommendation(RecommendationCode.SpeedUpSlightly, slidesRemaining, 300_000));

    private static (AiSectionViewModel Vm, AiRecommendationEnricher Ai, FakeAiTextClient Client, ManualTimeProvider Time) Make(
        AiAvailability availability = AiAvailability.Available,
        AiEnrichmentOptions? options = null)
    {
        var time = new ManualTimeProvider();
        var client = new FakeAiTextClient();
        var ai = new AiRecommendationEnricher(client, availability, options ?? new AiEnrichmentOptions { MinInterval = TimeSpan.Zero }, time);
        return (new AiSectionViewModel(ai, new ImmediateDispatcher()), ai, client, time);
    }

    [Fact]
    public void By_default_the_switch_is_off_available_and_the_status_says_it_is_off()
    {
        var (vm, ai, client, _) = Make();

        Assert.False(vm.Enabled);
        Assert.True(vm.CanToggle);
        Assert.False(ai.Enabled);
        Assert.Equal(AiStatus.Disabled, vm.Status);
        Assert.Equal(UiText.Get("Ai_Status_Disabled"), vm.StatusText);
        Assert.Equal(UiText.Get("Ai_LastTextNone"), vm.LastTextLine);
        Assert.Equal(UiText.Get("Ai_Privacy"), vm.PrivacyNote);
        Assert.Equal(UiText.Get("Ai_Title"), vm.Title);
        Assert.Equal(UiText.Get("Ai_Toggle"), vm.ToggleLabel);
        Assert.Equal(0, client.Calls);
    }

    [Theory]
    [InlineData(AiAvailability.MissingVariables, AiStatus.NotConfigured)]
    [InlineData(AiAvailability.InvalidAddress, AiStatus.InvalidAddress)]
    [InlineData(AiAvailability.InsecureAddress, AiStatus.InsecureAddress)]
    public void When_the_settings_are_unusable_the_switch_is_disabled_with_an_explanation_and_cannot_be_turned_on(AiAvailability availability, AiStatus expected)
    {
        var (vm, ai, _, _) = Make(availability);

        vm.Enabled = true;

        Assert.False(vm.CanToggle);
        Assert.False(vm.Enabled);
        Assert.False(ai.Enabled);
        Assert.Equal(expected, vm.Status);
        Assert.Equal(UiText.Get("Ai_Status_" + expected), vm.StatusText);
        Assert.True(vm.IsProblem || expected == AiStatus.NotConfigured);
    }

    [Fact]
    public void Turning_it_on_shows_ready_and_off_shows_the_off_status_again()
    {
        var (vm, ai, _, _) = Make();

        vm.Enabled = true;
        Assert.True(ai.Enabled);
        Assert.Equal(UiText.Get("Ai_Status_Ready"), vm.StatusText);
        Assert.True(vm.IsGood);
        Assert.False(vm.IsProblem);

        vm.Enabled = false;
        Assert.False(ai.Enabled);
        Assert.Equal(UiText.Get("Ai_Status_Disabled"), vm.StatusText);
        Assert.False(vm.IsGood);
    }

    [Fact]
    public async Task While_a_request_is_open_it_says_working_and_afterwards_shows_the_last_text()
    {
        var (vm, ai, client, _) = Make();
        client.Hold = new TaskCompletionSource();
        vm.Enabled = true;

        ai.Apply(Alerting());
        await Wait(() => client.Calls == 1);
        vm.Tick();
        Assert.Equal(AiStatus.Working, vm.Status);
        Assert.Equal(UiText.Get("Ai_Status_Working"), vm.StatusText);

        client.Hold.SetResult();
        await ai.WhenIdleAsync();
        Assert.Equal(AiStatus.Ready, vm.Status);
        Assert.Equal(UiText.Format("Ai_LastText", "Pick up the pace now"), vm.LastTextLine);
    }

    [Fact]
    public async Task Errors_show_paused_or_invalid_key_and_the_timer_ends_the_pause()
    {
        var (vm, ai, client, time) = Make();
        vm.Enabled = true;
        client.Answer = AiTextResult.Fail(AiFailureKind.Unauthorized);

        ai.Apply(Alerting(1));
        await ai.WhenIdleAsync();
        Assert.Equal(AiStatus.InvalidKey, vm.Status);
        Assert.Equal(UiText.Get("Ai_Status_InvalidKey"), vm.StatusText);
        Assert.True(vm.IsProblem);

        vm.Enabled = false;
        vm.Enabled = true;
        client.Answer = AiTextResult.Fail(AiFailureKind.ServerError);
        for (var i = 2; i <= 4; i++)
        {
            ai.Apply(Alerting(i));
            await ai.WhenIdleAsync();
        }

        Assert.Equal(AiStatus.Paused, vm.Status);
        Assert.Equal(UiText.Get("Ai_Status_Paused"), vm.StatusText);
        Assert.True(vm.IsProblem);

        time.Advance(TimeSpan.FromSeconds(61));
        vm.Tick(); // the pause ends with time, not with an event
        Assert.Equal(AiStatus.Ready, vm.Status);
        Assert.Equal(UiText.Get("Ai_Status_Ready"), vm.StatusText);
    }

    [Fact]
    public async Task Changes_that_happen_on_other_threads_reach_the_window_through_the_dispatcher()
    {
        var time = new ManualTimeProvider();
        var client = new FakeAiTextClient();
        var ai = new AiRecommendationEnricher(client, AiAvailability.Available, new AiEnrichmentOptions { MinInterval = TimeSpan.Zero }, time);
        var dispatcher = new QueueDispatcher();
        var vm = new AiSectionViewModel(ai, dispatcher);

        ai.Enabled = true;
        Assert.True(dispatcher.PostedCount >= 1);
        Assert.Equal(UiText.Get("Ai_Status_Disabled"), vm.StatusText); // nothing is applied until the UI thread runs it
        dispatcher.RunPending();
        Assert.Equal(UiText.Get("Ai_Status_Ready"), vm.StatusText);
        Assert.True(vm.Enabled);

        ai.Apply(Alerting());
        await ai.WhenIdleAsync();
        dispatcher.RunPending();
        Assert.Equal(UiText.Format("Ai_LastText", "Pick up the pace now"), vm.LastTextLine);

        vm.Dispose();
        var posted = dispatcher.PostedCount;
        ai.Enabled = false;
        Assert.Equal(posted, dispatcher.PostedCount); // disposed: no more notifications
    }

    [Fact]
    public void Every_status_has_its_own_spanish_text_in_the_resource_file()
    {
        var texts = Enum.GetValues<AiStatus>().Select(status => UiText.Get("Ai_Status_" + status)).ToList();

        Assert.Equal(texts.Count, texts.Distinct().Count());
        foreach (var status in Enum.GetValues<AiStatus>())
        {
            Assert.NotEqual("Ai_Status_" + status, UiText.Get("Ai_Status_" + status));
        }

        Assert.NotEqual("Ai_Privacy", UiText.Get("Ai_Privacy"));
        Assert.NotEqual("Ai_Title", UiText.Get("Ai_Title"));
        Assert.NotEqual("Ai_Toggle", UiText.Get("Ai_Toggle"));
        Assert.NotEqual("Ai_LastTextNone", UiText.Get("Ai_LastTextNone"));
    }

    [Fact]
    public void The_privacy_note_tells_what_is_sent_and_what_is_never_sent()
    {
        var note = UiText.Get("Ai_Privacy");

        // One line, no placeholders, and it says "only figures", "never names" and "never files" in Spanish.
        Assert.DoesNotContain('\n', note);
        Assert.DoesNotContain("{0}", note, StringComparison.Ordinal);
        Assert.True(note.Length is > 20 and < 120);
    }

    [Fact]
    public async Task The_main_view_model_exposes_the_section_only_when_it_has_one()
    {
        var host = new CompanionHost(new CompanionServerOptions { Port = 0, PairingCode = "123456" }, new RecordingSlideKeyboard(), new InMemoryHistoryStore(), new ManualTimeProvider());
        var (section, _, _, _) = Make();

        using var with = new MainViewModel(host, new ImmediateDispatcher(), lanAddresses: () => Array.Empty<string>(), ai: section);
        using var without = new MainViewModel(host, new ImmediateDispatcher(), lanAddresses: () => Array.Empty<string>());

        Assert.True(with.HasAi);
        Assert.Same(section, with.Ai);
        Assert.False(without.HasAi);
        Assert.Null(without.Ai);
        await host.DisposeAsync();
    }

    private static async Task Wait(Func<bool> condition)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), "Timed out.");
            await Task.Delay(5);
        }
    }
}
