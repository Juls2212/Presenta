namespace Presenta.Domain.Tests.Support;

/// <summary>A controllable time provider. Time only moves when <see cref="Advance"/> is called.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private long _ticks;
    private readonly DateTimeOffset _start;

    public FakeTimeProvider(DateTimeOffset? start = null)
    {
        _start = start ?? new DateTimeOffset(2026, 10, 31, 15, 0, 0, TimeSpan.Zero);
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public override DateTimeOffset GetUtcNow() => _start.AddTicks(_ticks);

    public void Advance(TimeSpan amount) => _ticks += amount.Ticks;
}
