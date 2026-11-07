using Presenta.Domain.Tests.Support;

namespace Presenta.Domain.Tests;

public class RecommendationTests
{
    private readonly RuleBasedRecommendationProvider _provider = new();

    private static RecommendationContext Context(
        SessionState state = SessionState.Running,
        PaceStatus pace = PaceStatus.OnTrack,
        double deviation = 0,
        int tolerance = 2,
        int slidesRemaining = 10,
        long timeRemainingMs = 600_000,
        long wrapUpMs = 60_000) =>
        new(state, pace, deviation, tolerance, slidesRemaining, timeRemainingMs, wrapUpMs);

    [Fact]
    public void On_track_when_pace_is_within_tolerance()
    {
        var result = _provider.Recommend(Context());

        Assert.Equal(RecommendationCode.OnTrack, result?.Code);
    }

    [Fact]
    public void Speed_up_slightly_when_moderately_behind()
    {
        var result = _provider.Recommend(Context(pace: PaceStatus.Behind, deviation: -3));

        Assert.Equal(RecommendationCode.SpeedUpSlightly, result?.Code);
    }

    [Fact]
    public void Speed_up_slightly_at_exactly_twice_the_tolerance()
    {
        var result = _provider.Recommend(Context(pace: PaceStatus.Behind, deviation: -4));

        Assert.Equal(RecommendationCode.SpeedUpSlightly, result?.Code);
    }

    [Fact]
    public void Speed_up_now_when_far_behind()
    {
        var result = _provider.Recommend(Context(pace: PaceStatus.Behind, deviation: -4.5));

        Assert.Equal(RecommendationCode.SpeedUpNow, result?.Code);
    }

    [Fact]
    public void Speed_up_now_threshold_is_configurable()
    {
        var provider = new RuleBasedRecommendationProvider(new RecommendationOptions { SpeedUpNowToleranceMultiplier = 1 });

        var result = provider.Recommend(Context(pace: PaceStatus.Behind, deviation: -3));

        Assert.Equal(RecommendationCode.SpeedUpNow, result?.Code);
    }

    [Fact]
    public void Slow_down_when_too_fast()
    {
        var result = _provider.Recommend(Context(pace: PaceStatus.TooFast, deviation: 4));

        Assert.Equal(RecommendationCode.SlowDown, result?.Code);
    }

    [Fact]
    public void Wrap_up_when_remaining_time_is_within_the_critical_threshold()
    {
        var result = _provider.Recommend(Context(pace: PaceStatus.Behind, deviation: -6, timeRemainingMs: 60_000));

        Assert.Equal(RecommendationCode.WrapUp, result?.Code);
    }

    [Fact]
    public void Wrap_up_on_the_last_slide()
    {
        var result = _provider.Recommend(Context(slidesRemaining: 0));

        Assert.Equal(RecommendationCode.WrapUp, result?.Code);
    }

    [Fact]
    public void Overtime_when_remaining_time_is_negative()
    {
        var result = _provider.Recommend(Context(timeRemainingMs: -5_000, slidesRemaining: 3));

        Assert.Equal(RecommendationCode.Overtime, result?.Code);
        Assert.Equal(-5_000, result?.TimeRemainingMs);
        Assert.Equal(3, result?.SlidesRemaining);
    }

    [Fact]
    public void No_recommendation_while_there_is_not_enough_data()
    {
        Assert.Null(_provider.Recommend(Context(pace: PaceStatus.NotEnoughData)));
    }

    [Theory]
    [InlineData(SessionState.Configured)]
    [InlineData(SessionState.Finished)]
    public void No_recommendation_before_start_or_after_finish(SessionState state)
    {
        Assert.Null(_provider.Recommend(Context(state: state)));
    }

    [Fact]
    public void Paused_sessions_still_get_a_recommendation()
    {
        Assert.NotNull(_provider.Recommend(Context(state: SessionState.Paused)));
    }

    [Fact]
    public void Free_text_is_always_null_in_version_one()
    {
        foreach (var pace in Enum.GetValues<PaceStatus>())
        {
            var result = _provider.Recommend(Context(pace: pace, deviation: -5));
            Assert.Null(result?.FreeText);
        }
    }

    [Fact]
    public void Snapshot_carries_the_recommendation_with_numeric_parameters()
    {
        var clock = new FakeTimeProvider();
        var session = TestSessions.Started(clock, slides: 20, minutes: 20);
        clock.Advance(TimeSpan.FromMinutes(10)); // expected slide 10.5, current slide 1: far behind

        var recommendation = session.GetSnapshot().Recommendation;

        Assert.NotNull(recommendation);
        Assert.Equal(RecommendationCode.SpeedUpNow, recommendation.Code);
        Assert.Equal(19, recommendation.SlidesRemaining);
        Assert.Equal(600_000, recommendation.TimeRemainingMs);
        Assert.Null(recommendation.FreeText);
    }

    [Fact]
    public void Snapshot_has_no_recommendation_during_the_warm_up()
    {
        var clock = new FakeTimeProvider();
        var session = TestSessions.Started(clock);
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Null(session.GetSnapshot().Recommendation);
    }

    [Fact]
    public void A_custom_provider_can_be_plugged_in()
    {
        var clock = new FakeTimeProvider();
        var config = SessionConfiguration.Create(10, TimeSpan.FromMinutes(10)).Value!;
        var session = new PresentationSession(config, clock, recommendations: new FixedProvider());
        session.Start();

        var recommendation = session.GetSnapshot().Recommendation;

        Assert.Equal(RecommendationCode.SlowDown, recommendation?.Code);
    }

    private sealed class FixedProvider : IRecommendationProvider
    {
        public Recommendation? Recommend(RecommendationContext context) =>
            new(RecommendationCode.SlowDown, context.SlidesRemaining, context.TimeRemainingMs);
    }
}
