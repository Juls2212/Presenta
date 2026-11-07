namespace Presenta.TestSupport;

/// <summary>
/// A <see cref="TimeProvider"/> that only moves when <see cref="Advance"/> is called. Timers created through
/// <see cref="CreateTimer"/> (and therefore <c>Task.Delay(delay, timeProvider)</c>) fire, in order, as time passes.
/// Callbacks run synchronously on the thread that calls <see cref="Advance"/>.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = new();
    private readonly DateTimeOffset _start;
    private long _ticks;

    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        _start = start ?? new DateTimeOffset(2026, 11, 2, 14, 30, 0, TimeSpan.Zero);
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _ticks;
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _start.AddTicks(_ticks);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Moves time forward, firing every timer that becomes due, earliest first.</summary>
    public void Advance(TimeSpan amount)
    {
        long target;
        lock (_gate)
        {
            target = _ticks + amount.Ticks;
        }

        while (true)
        {
            ManualTimer? next = null;
            long dueAt = long.MaxValue;
            lock (_gate)
            {
                foreach (var timer in _timers)
                {
                    if (timer.NextDue is { } due && due <= target && due < dueAt)
                    {
                        next = timer;
                        dueAt = due;
                    }
                }

                if (next is null)
                {
                    _ticks = target;
                    return;
                }

                _ticks = Math.Max(_ticks, dueAt);
                next.Rearm();
            }

            next.Fire();
        }
    }

    private long NowTicks()
    {
        lock (_gate)
        {
            return _ticks;
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private long _period;
        private long? _nextDue;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public long? NextDue => _nextDue;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
            _nextDue = dueTime == Timeout.InfiniteTimeSpan ? null : _owner.NowTicks() + dueTime.Ticks;
            return true;
        }

        /// <summary>Called with the owner's lock held, before <see cref="Fire"/>.</summary>
        public void Rearm() => _nextDue = _period > 0 ? _nextDue + _period : null;

        public void Fire() => _callback(_state);

        public void Dispose()
        {
            _nextDue = null;
            _owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
