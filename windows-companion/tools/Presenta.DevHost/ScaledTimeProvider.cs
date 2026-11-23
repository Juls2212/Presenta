namespace Presenta.DevHost;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock and timers run <c>scale</c> times faster than the inner one. Only the
/// developer host uses it, to watch every alert of a long session within seconds; the shipped app never does.
/// </summary>
internal sealed class ScaledTimeProvider : TimeProvider
{
    private readonly TimeProvider _inner;
    private readonly long _scale;
    private readonly DateTimeOffset _startUtc;
    private readonly long _startTimestamp;

    public ScaledTimeProvider(TimeProvider inner, int scale)
    {
        if (scale < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "The scale must be at least 1.");
        }

        _inner = inner;
        _scale = scale;
        _startUtc = inner.GetUtcNow();
        _startTimestamp = inner.GetTimestamp();
    }

    public override long TimestampFrequency => _inner.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    public override long GetTimestamp() => _startTimestamp + (_inner.GetTimestamp() - _startTimestamp) * _scale;

    public override DateTimeOffset GetUtcNow() => _startUtc + (_inner.GetUtcNow() - _startUtc) * _scale;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        _inner.CreateTimer(callback, state, Shrink(dueTime), Shrink(period));

    // A timer that should fire after d of scaled time fires after d / scale of real time. Infinite stays infinite.
    private TimeSpan Shrink(TimeSpan value) => value == Timeout.InfiniteTimeSpan ? value : value / _scale;
}
