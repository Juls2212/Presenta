using Presenta.Domain;
using Presenta.Domain.History;
using Presenta.Domain.Keyboard;

namespace Presenta.TestSupport;

/// <summary>Fake keyboard that records every delivered key instead of pressing keys.</summary>
public sealed class RecordingSlideKeyboard : ISlideKeyboard
{
    private readonly object _gate = new();
    private readonly List<SlideDirection> _sent = new();
    private int _attempts;

    /// <summary>Keys the keyboard reported as delivered.</summary>
    public IReadOnlyList<SlideDirection> Sent
    {
        get
        {
            lock (_gate)
            {
                return _sent.ToArray();
            }
        }
    }

    /// <summary>How many times <see cref="Send"/> was called, delivered or not.</summary>
    public int Attempts
    {
        get
        {
            lock (_gate)
            {
                return _attempts;
            }
        }
    }

    /// <summary>When set, <see cref="Send"/> throws it, to simulate an unexpected failure.</summary>
    public Exception? FailWith { get; set; }

    /// <summary>What <see cref="Send"/> returns. Anything but Delivered records no key.</summary>
    public KeyDeliveryResult Result { get; set; } = KeyDeliveryResult.Delivered;

    /// <summary>What <see cref="GetTargetState"/> returns.</summary>
    public TargetState State { get; set; } = TargetState.Ready;

    public KeyDeliveryResult Send(SlideDirection direction)
    {
        lock (_gate)
        {
            _attempts++;
        }

        if (FailWith is not null)
        {
            throw FailWith;
        }

        if (Result == KeyDeliveryResult.Delivered)
        {
            lock (_gate)
            {
                _sent.Add(direction);
            }
        }

        return Result;
    }

    public TargetState GetTargetState() => State;
}

/// <summary>History store that keeps records in memory.</summary>
public sealed class InMemoryHistoryStore : IHistoryStore
{
    private readonly object _gate = new();
    private readonly List<SessionSummary> _records = new();

    public IReadOnlyList<SessionSummary> Records
    {
        get
        {
            lock (_gate)
            {
                return _records.ToArray();
            }
        }
    }

    public Task SaveAsync(SessionSummary summary, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _records.Add(summary);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SessionSummary>> ListNewestFirstAsync(int limit, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<SessionSummary> result = _records
                .OrderByDescending(r => r.FinishedAtUtc)
                .Take(limit)
                .ToArray();
            return Task.FromResult(result);
        }
    }
}
