using System.Security.Cryptography;
using System.Text;

namespace Presenta.Server;

internal enum PairingOutcome
{
    Accepted,
    WrongCode,
    LockedOut,
}

/// <summary>
/// Holds the pairing code and rejects brute force: after <c>threshold</c> consecutive wrong codes from one address,
/// that address is locked out for <c>duration</c>. The code is compared in constant time and is never logged.
/// </summary>
internal sealed class PairingGate
{
    private const int MaxTrackedAddresses = 1024;

    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset? LockedUntil;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly int _threshold;
    private readonly TimeSpan _duration;
    private string _code;

    public PairingGate(string? fixedCode, int threshold, TimeSpan duration)
    {
        _threshold = threshold;
        _duration = duration;
        _code = fixedCode ?? NewCode();
    }

    public string Code
    {
        get
        {
            lock (_gate)
            {
                return _code;
            }
        }
    }

    public void Regenerate()
    {
        lock (_gate)
        {
            string next;
            do
            {
                next = NewCode();
            }
            while (next == _code);
            _code = next;
        }
    }

    public PairingOutcome Check(string remoteKey, string supplied, DateTimeOffset now, out TimeSpan retryAfter)
    {
        lock (_gate)
        {
            retryAfter = TimeSpan.Zero;
            if (_entries.Count > MaxTrackedAddresses)
            {
                foreach (var key in _entries.Where(e => e.Value.LockedUntil is null || e.Value.LockedUntil <= now).Select(e => e.Key).ToList())
                {
                    _entries.Remove(key);
                }
            }

            if (!_entries.TryGetValue(remoteKey, out var entry))
            {
                entry = new Entry();
                _entries[remoteKey] = entry;
            }

            if (entry.LockedUntil is { } until)
            {
                if (until > now)
                {
                    retryAfter = until - now;
                    return PairingOutcome.LockedOut;
                }

                entry.LockedUntil = null;
                entry.Failures = 0;
            }

            var expected = Encoding.UTF8.GetBytes(_code);
            var actual = Encoding.UTF8.GetBytes(supplied);
            if (expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                _entries.Remove(remoteKey);
                return PairingOutcome.Accepted;
            }

            entry.Failures++;
            if (entry.Failures >= _threshold)
            {
                entry.LockedUntil = now + _duration;
                entry.Failures = 0;
            }

            return PairingOutcome.WrongCode;
        }
    }

    private static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
}
