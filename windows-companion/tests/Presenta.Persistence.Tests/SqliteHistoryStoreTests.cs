using Presenta.Domain;
using Presenta.Persistence;

namespace Presenta.Persistence.Tests;

public class SqliteHistoryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "presenta-persistence-" + Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_directory, "history.db");

    private static SessionSummary Summary(string name, DateTimeOffset finishedAt, FinalState state = FinalState.FinishedManually) =>
        new(name, finishedAt, 900_000, 872_000, 20, 18, 1.2375, state, true);

    private static readonly DateTimeOffset Noon = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_database_and_its_folder_are_created_on_first_use()
    {
        Assert.False(Directory.Exists(_directory));

        await SqliteHistoryStore.OpenAsync(DatabasePath);

        Assert.True(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task A_saved_record_comes_back_with_every_field_intact()
    {
        var store = await SqliteHistoryStore.OpenAsync(DatabasePath);
        var saved = new SessionSummary("Demo", Noon.AddMilliseconds(123), 900_000, 872_500, 20, 18, 1.2375, FinalState.EndedWhilePaused, false);

        await store.SaveAsync(saved);
        var loaded = Assert.Single(await store.ListNewestFirstAsync(10));

        Assert.Equal(saved, loaded);
    }

    [Fact]
    public async Task Every_final_state_round_trips()
    {
        var store = await SqliteHistoryStore.OpenAsync(DatabasePath);
        foreach (var (state, index) in Enum.GetValues<FinalState>().Select((s, i) => (s, i)))
        {
            await store.SaveAsync(Summary(state.ToString(), Noon.AddMinutes(index), state));
        }

        var loaded = await store.ListNewestFirstAsync(10);

        Assert.Equal(
            Enum.GetValues<FinalState>().Reverse(),
            loaded.Select(r => r.FinalState));
    }

    [Fact]
    public async Task Records_are_listed_newest_first()
    {
        var store = await SqliteHistoryStore.OpenAsync(DatabasePath);
        await store.SaveAsync(Summary("middle", Noon));
        await store.SaveAsync(Summary("oldest", Noon.AddDays(-1)));
        await store.SaveAsync(Summary("newest", Noon.AddDays(1)));

        var loaded = await store.ListNewestFirstAsync(10);

        Assert.Equal(new[] { "newest", "middle", "oldest" }, loaded.Select(r => r.Name));
    }

    [Fact]
    public async Task Records_with_the_same_finish_time_keep_the_most_recently_saved_first()
    {
        var store = await SqliteHistoryStore.OpenAsync(DatabasePath);
        await store.SaveAsync(Summary("saved first", Noon));
        await store.SaveAsync(Summary("saved second", Noon));

        var loaded = await store.ListNewestFirstAsync(10);

        Assert.Equal(new[] { "saved second", "saved first" }, loaded.Select(r => r.Name));
    }

    [Fact]
    public async Task The_limit_caps_the_number_of_records()
    {
        var store = await SqliteHistoryStore.OpenAsync(DatabasePath);
        for (var i = 0; i < 5; i++)
        {
            await store.SaveAsync(Summary("r" + i, Noon.AddMinutes(i)));
        }

        var loaded = await store.ListNewestFirstAsync(3);

        Assert.Equal(new[] { "r4", "r3", "r2" }, loaded.Select(r => r.Name));
    }

    [Fact]
    public async Task The_default_limit_is_fifty()
    {
        Assert.Equal(50, HistoryStoreDefaults.DefaultLimit);
        var store = await SqliteHistoryStore.OpenAsync(DatabasePath);
        for (var i = 0; i < 55; i++)
        {
            await store.SaveAsync(Summary("r" + i, Noon.AddMinutes(i)));
        }

        var loaded = await store.ListNewestFirstAsync(HistoryStoreDefaults.DefaultLimit);

        Assert.Equal(50, loaded.Count);
        Assert.Equal("r54", loaded[0].Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_limit_below_one_is_rejected(int limit)
    {
        var store = await SqliteHistoryStore.OpenAsync(DatabasePath);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ListNewestFirstAsync(limit));
    }

    [Fact]
    public async Task An_empty_database_lists_nothing()
    {
        var store = await SqliteHistoryStore.OpenAsync(DatabasePath);

        Assert.Empty(await store.ListNewestFirstAsync(10));
    }

    [Fact]
    public async Task Records_survive_closing_and_reopening_the_database()
    {
        var first = await SqliteHistoryStore.OpenAsync(DatabasePath);
        await first.SaveAsync(Summary("kept", Noon));

        var reopened = await SqliteHistoryStore.OpenAsync(DatabasePath);
        var loaded = Assert.Single(await reopened.ListNewestFirstAsync(10));

        Assert.Equal("kept", loaded.Name);
        await reopened.SaveAsync(Summary("added later", Noon.AddHours(1)));
        Assert.Equal(2, (await first.ListNewestFirstAsync(10)).Count);
    }

    [Fact]
    public async Task The_file_is_not_held_open_between_calls()
    {
        var store = await SqliteHistoryStore.OpenAsync(DatabasePath);
        await store.SaveAsync(Summary("x", Noon));

        File.Move(DatabasePath, DatabasePath + ".moved"); // fails if a connection still holds the file

        Assert.True(File.Exists(DatabasePath + ".moved"));
    }

    [Fact]
    public async Task Concurrent_saves_are_all_stored()
    {
        var store = await SqliteHistoryStore.OpenAsync(DatabasePath);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => store.SaveAsync(Summary("c" + i, Noon.AddSeconds(i)))));

        Assert.Equal(20, (await store.ListNewestFirstAsync(50)).Count);
    }

    [Fact]
    public async Task Names_with_non_ascii_text_survive()
    {
        var store = await SqliteHistoryStore.OpenAsync(DatabasePath);
        var name = "Name ü 中";
        await store.SaveAsync(Summary(name, Noon));

        Assert.Equal(name, Assert.Single(await store.ListNewestFirstAsync(1)).Name);
    }

    [Fact]
    public void The_default_path_is_under_the_local_application_data_folder()
    {
        var path = HistoryStoreDefaults.DefaultDatabasePath();

        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), path);
        Assert.EndsWith(Path.Combine("Presenta", "history.db"), path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
