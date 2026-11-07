using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Presenta.Domain;
using Presenta.Domain.History;

namespace Presenta.Persistence;

/// <summary>Defaults of the history database.</summary>
public static class HistoryStoreDefaults
{
    /// <summary>Number of records returned when a caller does not ask for a specific limit.</summary>
    public const int DefaultLimit = 50;

    /// <summary><c>%LOCALAPPDATA%\Presenta\history.db</c>.</summary>
    public static string DefaultDatabasePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Presenta", "history.db");
}

/// <summary>One row of the history table. Times are stored as UTC Unix milliseconds so that SQLite can order them.</summary>
internal sealed class HistoryRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public long FinishedAtUnixMs { get; set; }

    public long PlannedDurationMs { get; set; }

    public long ActualDurationMs { get; set; }

    public int PlannedSlides { get; set; }

    public int RegisteredSlides { get; set; }

    public double AverageSlidesPerMinute { get; set; }

    public int FinalState { get; set; }

    public bool EndedWithinPlannedTime { get; set; }
}

internal sealed class HistoryDbContext : DbContext
{
    public HistoryDbContext(DbContextOptions<HistoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<HistoryRecord> Records => Set<HistoryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var record = modelBuilder.Entity<HistoryRecord>();
        record.ToTable("history");
        record.HasKey(r => r.Id);
        record.Property(r => r.Name).IsRequired();
        record.HasIndex(r => r.FinishedAtUnixMs);
    }
}

/// <summary>
/// History in a SQLite file through EF Core. The schema is created with <c>EnsureCreated</c> (prototype; see ADR on schema
/// evolution): a later schema change needs a migration or a new file. A short-lived context is used per operation and
/// connection pooling is off, so the file is never held open between calls.
/// </summary>
public sealed class SqliteHistoryStore : IHistoryStore
{
    private readonly DbContextOptions<HistoryDbContext> _options;

    private SqliteHistoryStore(string databasePath)
    {
        DatabasePath = databasePath;
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString();
        _options = new DbContextOptionsBuilder<HistoryDbContext>().UseSqlite(connectionString).Options;
    }

    public string DatabasePath { get; }

    /// <summary>Opens (and creates, if needed) the database at <paramref name="databasePath"/>.</summary>
    public static async Task<SqliteHistoryStore> OpenAsync(string? databasePath = null, CancellationToken cancellationToken = default)
    {
        var path = databasePath ?? HistoryStoreDefaults.DefaultDatabasePath();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var store = new SqliteHistoryStore(path);
        await using var context = new HistoryDbContext(store._options);
        await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        return store;
    }

    public async Task SaveAsync(SessionSummary summary, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(summary);
        await using var context = new HistoryDbContext(_options);
        context.Records.Add(new HistoryRecord
        {
            Name = summary.Name,
            FinishedAtUnixMs = summary.FinishedAtUtc.ToUnixTimeMilliseconds(),
            PlannedDurationMs = summary.PlannedDurationMs,
            ActualDurationMs = summary.ActualDurationMs,
            PlannedSlides = summary.PlannedSlides,
            RegisteredSlides = summary.RegisteredSlides,
            AverageSlidesPerMinute = summary.AverageSlidesPerMinute,
            FinalState = (int)summary.FinalState,
            EndedWithinPlannedTime = summary.EndedWithinPlannedTime,
        });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SessionSummary>> ListNewestFirstAsync(int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        await using var context = new HistoryDbContext(_options);
        var rows = await context.Records
            .AsNoTracking()
            .OrderByDescending(r => r.FinishedAtUnixMs)
            .ThenByDescending(r => r.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ConvertAll(r => new SessionSummary(
            r.Name,
            DateTimeOffset.FromUnixTimeMilliseconds(r.FinishedAtUnixMs),
            r.PlannedDurationMs,
            r.ActualDurationMs,
            r.PlannedSlides,
            r.RegisteredSlides,
            r.AverageSlidesPerMinute,
            (FinalState)r.FinalState,
            r.EndedWithinPlannedTime));
    }
}
