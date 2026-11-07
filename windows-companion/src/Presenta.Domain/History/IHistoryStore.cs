namespace Presenta.Domain.History;

/// <summary>Storage of finished sessions. Implemented by the persistence project; faked in tests.</summary>
public interface IHistoryStore
{
    /// <summary>Stores one finished session.</summary>
    Task SaveAsync(SessionSummary summary, CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent sessions, newest first, at most <paramref name="limit"/> of them.</summary>
    Task<IReadOnlyList<SessionSummary>> ListNewestFirstAsync(int limit, CancellationToken cancellationToken = default);
}
