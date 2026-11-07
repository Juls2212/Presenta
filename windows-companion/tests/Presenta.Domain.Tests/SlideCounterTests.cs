using Presenta.Domain.Keyboard;
using Presenta.Domain.Tests.Support;

namespace Presenta.Domain.Tests;

public class SlideCounterTests
{
    private readonly FakeTimeProvider _clock = new();

    [Fact]
    public void Counter_starts_at_slide_one()
    {
        var session = TestSessions.Started(_clock, slides: 5);

        Assert.Equal(1, session.CurrentSlide);
    }

    [Fact]
    public void Next_advances_and_reports_counter_changed()
    {
        var session = TestSessions.Started(_clock, slides: 5);

        var result = session.Next();

        Assert.True(result.Succeeded);
        Assert.True(result.CounterChanged);
        Assert.Equal(2, result.CurrentSlide);
    }

    [Fact]
    public void Next_at_last_slide_succeeds_without_changing_the_counter()
    {
        var session = TestSessions.Started(_clock, slides: 3);
        session.SetSlide(3);

        var result = session.Next();

        Assert.True(result.Succeeded);
        Assert.False(result.CounterChanged);
        Assert.Equal(3, result.CurrentSlide);
    }

    [Fact]
    public void Previous_goes_back_and_reports_counter_changed()
    {
        var session = TestSessions.Started(_clock, slides: 5);
        session.SetSlide(4);

        var result = session.Previous();

        Assert.True(result.CounterChanged);
        Assert.Equal(3, result.CurrentSlide);
    }

    [Fact]
    public void Previous_at_first_slide_succeeds_without_changing_the_counter()
    {
        var session = TestSessions.Started(_clock, slides: 5);

        var result = session.Previous();

        Assert.True(result.Succeeded);
        Assert.False(result.CounterChanged);
        Assert.Equal(1, result.CurrentSlide);
    }

    [Fact]
    public void Single_slide_session_never_changes_the_counter()
    {
        var session = TestSessions.Started(_clock, slides: 1);

        Assert.False(session.Next().CounterChanged);
        Assert.False(session.Previous().CounterChanged);
        Assert.Equal(1, session.CurrentSlide);
    }

    [Fact]
    public void SetSlide_within_range_changes_the_counter()
    {
        var session = TestSessions.Started(_clock, slides: 10);

        var result = session.SetSlide(7);

        Assert.True(result.Succeeded);
        Assert.True(result.CounterChanged);
        Assert.Equal(7, session.CurrentSlide);
    }

    [Fact]
    public void SetSlide_to_the_current_slide_reports_no_change()
    {
        var session = TestSessions.Started(_clock, slides: 10);
        session.SetSlide(4);

        var result = session.SetSlide(4);

        Assert.True(result.Succeeded);
        Assert.False(result.CounterChanged);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(11)]
    public void SetSlide_out_of_range_fails_and_keeps_the_counter(int slide)
    {
        var session = TestSessions.Started(_clock, slides: 10);
        session.SetSlide(5);

        var result = session.SetSlide(slide);

        Assert.False(result.Succeeded);
        Assert.False(result.CounterChanged);
        Assert.Equal(SessionErrorCode.SlideOutOfRange, result.Error);
        Assert.Equal(5, session.CurrentSlide);
    }

    [Fact]
    public void Slide_commands_are_accepted_while_paused()
    {
        var session = TestSessions.Started(_clock, slides: 10);
        session.Pause();

        Assert.True(session.Next().CounterChanged);
        Assert.True(session.SetSlide(6).CounterChanged);
        Assert.True(session.Previous().CounterChanged);
        Assert.Equal(5, session.CurrentSlide);
    }

    [Fact]
    public void Slide_commands_fail_before_the_session_starts()
    {
        var session = TestSessions.Configured(_clock);

        Assert.Equal(SessionErrorCode.InvalidState, session.Next().Error);
        Assert.Equal(1, session.CurrentSlide);
    }

    [Fact]
    public void Snapshot_reports_progress_ratio_and_slides_remaining()
    {
        var session = TestSessions.Started(_clock, slides: 20);
        session.SetSlide(5);

        var snapshot = session.GetSnapshot();

        Assert.Equal(0.25, snapshot.ProgressRatio, 10);
        Assert.Equal(15, snapshot.SlidesRemaining);
        Assert.Equal(20, snapshot.TotalSlides);
    }

    [Fact]
    public void WouldMove_predicts_the_counter_change_without_changing_anything()
    {
        var session = TestSessions.Started(_clock, slides: 3);

        Assert.True(session.WouldMove(SlideDirection.Next));
        Assert.False(session.WouldMove(SlideDirection.Previous));
        Assert.Equal(1, session.CurrentSlide);

        session.SetSlide(3);
        Assert.False(session.WouldMove(SlideDirection.Next));
        Assert.True(session.WouldMove(SlideDirection.Previous));
        Assert.Equal(3, session.CurrentSlide);
    }

    [Fact]
    public void WouldMove_is_false_before_the_session_runs_and_after_it_ends()
    {
        var configured = TestSessions.Configured(_clock, slides: 3);
        Assert.False(configured.WouldMove(SlideDirection.Next));

        var finished = TestSessions.Started(_clock, slides: 3);
        finished.Finish();
        Assert.False(finished.WouldMove(SlideDirection.Next));
    }

    [Fact]
    public void WouldMove_agrees_with_what_next_and_previous_really_do()
    {
        var session = TestSessions.Started(_clock, slides: 3);
        foreach (var direction in new[] { SlideDirection.Previous, SlideDirection.Next, SlideDirection.Next, SlideDirection.Next, SlideDirection.Previous })
        {
            var predicted = session.WouldMove(direction);
            var result = direction == SlideDirection.Next ? session.Next() : session.Previous();
            Assert.Equal(predicted, result.CounterChanged);
        }
    }
}
