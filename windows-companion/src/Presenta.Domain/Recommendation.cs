namespace Presenta.Domain;

/// <summary>A structured recommendation. It never contains display text.</summary>
/// <param name="Code">Recommendation code.</param>
/// <param name="SlidesRemaining">Slides left after the current one.</param>
/// <param name="TimeRemainingMs">Remaining time in milliseconds (negative in overtime).</param>
/// <param name="FreeText">Reserved for a future external provider. Always null in V1.</param>
public sealed record Recommendation(RecommendationCode Code, int SlidesRemaining, long TimeRemainingMs, string? FreeText = null);

/// <summary>Inputs available to a recommendation provider.</summary>
public sealed record RecommendationContext(
    SessionState State,
    PaceStatus PaceStatus,
    double SlideDeviation,
    int PaceTolerance,
    int SlidesRemaining,
    long TimeRemainingMs,
    long WrapUpThresholdMs);

/// <summary>Extension point for recommendation sources. V1 ships only <see cref="RuleBasedRecommendationProvider"/>.</summary>
public interface IRecommendationProvider
{
    /// <summary>Returns a recommendation, or null when none applies.</summary>
    Recommendation? Recommend(RecommendationContext context);
}

/// <summary>Thresholds used by <see cref="RuleBasedRecommendationProvider"/>.</summary>
public sealed record RecommendationOptions
{
    /// <summary>
    /// When the presenter is behind by more than this multiple of the pace tolerance the recommendation is
    /// <see cref="RecommendationCode.SpeedUpNow"/>; otherwise <see cref="RecommendationCode.SpeedUpSlightly"/>. Default 2.
    /// </summary>
    public double SpeedUpNowToleranceMultiplier { get; init; } = 2.0;
}

/// <summary>
/// Deterministic rules, evaluated in order: overtime; on the last slide or within the wrap-up threshold, wrap up;
/// not enough data, none; behind, speed up (slightly or now); too fast, slow down; otherwise on track.
/// Only running and paused sessions get a recommendation.
/// </summary>
public sealed class RuleBasedRecommendationProvider : IRecommendationProvider
{
    private readonly RecommendationOptions _options;

    public RuleBasedRecommendationProvider(RecommendationOptions? options = null)
    {
        _options = options ?? new RecommendationOptions();
    }

    public Recommendation? Recommend(RecommendationContext context)
    {
        if (context.State is not (SessionState.Running or SessionState.Paused))
        {
            return null;
        }

        RecommendationCode? code = null;

        if (context.TimeRemainingMs < 0)
        {
            code = RecommendationCode.Overtime;
        }
        else if (context.SlidesRemaining == 0 || context.TimeRemainingMs <= context.WrapUpThresholdMs)
        {
            code = RecommendationCode.WrapUp;
        }
        else
        {
            switch (context.PaceStatus)
            {
                case PaceStatus.Behind:
                    code = Math.Abs(context.SlideDeviation) > _options.SpeedUpNowToleranceMultiplier * context.PaceTolerance
                        ? RecommendationCode.SpeedUpNow
                        : RecommendationCode.SpeedUpSlightly;
                    break;
                case PaceStatus.TooFast:
                    code = RecommendationCode.SlowDown;
                    break;
                case PaceStatus.OnTrack:
                    code = RecommendationCode.OnTrack;
                    break;
            }
        }

        return code is null
            ? null
            : new Recommendation(code.Value, context.SlidesRemaining, context.TimeRemainingMs);
    }
}
