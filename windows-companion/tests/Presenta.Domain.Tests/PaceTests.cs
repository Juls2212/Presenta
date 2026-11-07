using Presenta.Domain.Tests.Support;

namespace Presenta.Domain.Tests;

public class PaceTests
{
    private static readonly SessionOptions Defaults = new();

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 1)]
    [InlineData(10, 1)]
    [InlineData(14, 1)]
    [InlineData(15, 2)]
    [InlineData(20, 2)]
    [InlineData(21, 2)]
    [InlineData(100, 10)]
    public void Tolerance_is_ten_percent_of_total_slides_with_a_minimum_of_one(int totalSlides, int expected)
    {
        Assert.Equal(expected, Defaults.GetPaceTolerance(totalSlides));
    }

    [Theory]
    [InlineData(9, PaceStatus.OnTrack)]   // deviation -2, exactly at the tolerance
    [InlineData(8, PaceStatus.Behind)]    // deviation -3
    [InlineData(13, PaceStatus.OnTrack)]  // deviation +2, exactly at the tolerance
    [InlineData(14, PaceStatus.TooFast)]  // deviation +3
    [InlineData(11, PaceStatus.OnTrack)]  // exactly as expected
    public void Pace_is_classified_at_the_tolerance_boundaries(int slide, PaceStatus expected)
    {
        // 21 slides over 20 minutes: after 10 minutes the expected slide is exactly 11 and the tolerance is 2.
        var result = PaceCalculator.Evaluate(Defaults, slide, 21, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20));

        Assert.Equal(expected, result.Status);
        Assert.Equal(2, result.Tolerance);
    }

    [Fact]
    public void Expected_slide_interpolates_linearly_from_one_to_the_total()
    {
        var planned = TimeSpan.FromMinutes(20);

        Assert.Equal(1, PaceCalculator.ExpectedSlide(21, TimeSpan.Zero, planned));
        Assert.Equal(11, PaceCalculator.ExpectedSlide(21, TimeSpan.FromMinutes(10), planned));
        Assert.Equal(21, PaceCalculator.ExpectedSlide(21, planned, planned));
    }

    [Fact]
    public void Expected_slide_is_clamped_after_the_planned_time()
    {
        var planned = TimeSpan.FromMinutes(20);

        Assert.Equal(21, PaceCalculator.ExpectedSlide(21, TimeSpan.FromMinutes(40), planned));
    }

    [Fact]
    public void Expected_slide_is_one_for_a_single_slide_session()
    {
        Assert.Equal(1, PaceCalculator.ExpectedSlide(1, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Status_is_not_enough_data_during_the_warm_up()
    {
        var planned = TimeSpan.FromMinutes(20);

        var early = PaceCalculator.Evaluate(Defaults, 20, 20, TimeSpan.FromSeconds(59.9), planned);
        var atWarmUpEnd = PaceCalculator.Evaluate(Defaults, 20, 20, TimeSpan.FromSeconds(60), planned);

        Assert.Equal(PaceStatus.NotEnoughData, early.Status);
        Assert.Equal(PaceStatus.TooFast, atWarmUpEnd.Status);
    }

    [Fact]
    public void Warm_up_is_the_smaller_of_sixty_seconds_and_ten_percent_of_planned_time()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), Defaults.GetWarmUp(TimeSpan.FromMinutes(20)));
        Assert.Equal(TimeSpan.FromSeconds(30), Defaults.GetWarmUp(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Session_snapshot_reports_not_enough_data_then_the_real_status()
    {
        var clock = new FakeTimeProvider();
        var session = TestSessions.Started(clock, slides: 20, minutes: 20);

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(PaceStatus.NotEnoughData, session.GetSnapshot().PaceStatus);

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(PaceStatus.Behind, session.GetSnapshot().PaceStatus);
    }

    [Fact]
    public void Slide_far_ahead_of_schedule_is_too_fast()
    {
        var clock = new FakeTimeProvider();
        var session = TestSessions.Started(clock, slides: 20, minutes: 20);
        clock.Advance(TimeSpan.FromMinutes(2));

        session.SetSlide(12);

        Assert.Equal(PaceStatus.TooFast, session.GetSnapshot().PaceStatus);
    }

    [Fact]
    public void Average_pace_is_zero_below_five_seconds_of_elapsed_time()
    {
        Assert.Equal(0, PaceCalculator.AverageSlidesPerMinute(Defaults, 5, TimeSpan.Zero));
        Assert.Equal(0, PaceCalculator.AverageSlidesPerMinute(Defaults, 5, TimeSpan.FromSeconds(4.9)));
    }

    [Fact]
    public void Average_pace_is_registered_slides_per_elapsed_minute_from_five_seconds()
    {
        Assert.Equal(12, PaceCalculator.AverageSlidesPerMinute(Defaults, 1, TimeSpan.FromSeconds(5)), 10);
        Assert.Equal(0.5, PaceCalculator.AverageSlidesPerMinute(Defaults, 3, TimeSpan.FromMinutes(6)), 10);
    }

    [Fact]
    public void Snapshot_average_pace_uses_elapsed_time_without_paused_time()
    {
        var clock = new FakeTimeProvider();
        var session = TestSessions.Started(clock, slides: 20, minutes: 20);
        session.SetSlide(3);
        clock.Advance(TimeSpan.FromMinutes(3));
        session.Pause();
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(1.0, session.GetSnapshot().AverageSlidesPerMinute, 10);
    }
}
