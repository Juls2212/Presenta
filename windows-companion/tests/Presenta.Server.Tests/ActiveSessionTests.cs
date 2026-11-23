using Presenta.Domain;
using Presenta.Domain.History;
using Presenta.Persistence;
using Presenta.Server.Tests.Support;
using Presenta.TestSupport;

namespace Presenta.Server.Tests;

/// <summary>Phase 8, item 2: the active session survives a crash and is recovered exactly once (ADR-67).</summary>
public class ActiveSessionTests
{
    private static ActiveSessionRecord Orphan(long elapsedMs = 125_000, bool paused = false) =>
        new("Orphan", 20, 900_000, 7, elapsedMs, paused, new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.Zero));

    private static string TempFile(string name) =>
        Path.Combine(Path.GetTempPath(), "presenta-tests-" + Guid.NewGuid().ToString("N"), name);

    // ---- writing while a session lives ----

    [Fact]
    public async Task Starting_a_session_writes_it_and_every_command_updates_the_file()
    {
        var store = new InMemoryActiveSessionStore();
        await using var harness = await ServerHarness.StartAsync(o => o with { HeartbeatTimeout = TimeSpan.FromHours(2) }, activeSessions: store);
        var client = await harness.ConnectAsync();

        await client.CommandAsync(Msg.Start(slides: 20, durationMs: 900_000, name: "Live"));
        Assert.Equal("Live", store.Record!.Name);
        Assert.Equal(1, store.Record.CurrentSlide);
        Assert.Equal(20, store.Record.PlannedSlides);
        Assert.False(store.Record.Paused);

        harness.Time.Advance(TimeSpan.FromSeconds(3));
        await client.CommandAsync(Msg.Next());
        await Wait.UntilAsync(() => store.Record!.CurrentSlide == 2, "the slide change to be written");
        Assert.True(store.Record!.ElapsedMs >= 3_000);

        await client.CommandAsync(Msg.Pause());
        await Wait.UntilAsync(() => store.Record!.Paused, "the pause to be written");
    }

    [Fact]
    public async Task A_running_session_is_written_at_least_every_five_seconds_even_without_commands()
    {
        var store = new InMemoryActiveSessionStore();
        await using var harness = await ServerHarness.StartAsync(o => o with { HeartbeatTimeout = TimeSpan.FromHours(2) }, activeSessions: store);
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 20, durationMs: 900_000));
        var before = store.Saves;

        for (var second = 0; second < 12; second++)
        {
            harness.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(15); // lets the tick callback finish
        }

        await Wait.UntilAsync(() => store.Saves >= before + 2, "periodic writes");
        Assert.True(store.Record!.ElapsedMs >= 5_000, "the written elapsed time follows the session clock");
        Assert.True(store.Record.ElapsedMs <= 12_000);
    }

    [Fact]
    public async Task Finishing_the_session_saves_one_history_record_and_removes_the_file()
    {
        var store = new InMemoryActiveSessionStore();
        await using var harness = await ServerHarness.StartAsync(activeSessions: store);
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 5, durationMs: 60_000));
        Assert.NotNull(store.Record);

        await client.CommandAsync(Msg.Finish());

        Assert.Null(store.Record);
        Assert.Single(harness.Memory.Records);
        Assert.Equal(FinalState.FinishedManually, harness.Memory.Records[0].FinalState);
    }

    [Fact]
    public async Task Ending_as_interrupted_by_the_grace_period_or_by_closing_the_window_also_removes_the_file()
    {
        var store = new InMemoryActiveSessionStore();
        await using var harness = await ServerHarness.StartAsync(
            o => o with { DisconnectGracePeriod = TimeSpan.FromSeconds(10), HeartbeatTimeout = TimeSpan.FromHours(2) },
            activeSessions: store);
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 5, durationMs: 60_000));
        await client.DisposeAsync();
        await Wait.UntilAsync(() => !harness.Service.Status.WatchConnected, "the watch to be seen as gone");

        harness.Time.Advance(TimeSpan.FromSeconds(12));
        await Wait.UntilAsync(() => harness.Memory.Records.Count == 1, "the grace period to end the session");

        Assert.Null(store.Record);
        Assert.Equal(FinalState.InterruptedByDisconnection, harness.Memory.Records[0].FinalState);

        var second = await harness.ConnectAsync();
        await second.CommandAsync(Msg.Start(slides: 5, durationMs: 60_000));
        Assert.NotNull(store.Record);
        Assert.True(await harness.Service.InterruptActiveSessionAsync());
        Assert.Null(store.Record);
        Assert.Equal(2, harness.Memory.Records.Count);
    }

    [Fact]
    public async Task A_history_that_cannot_save_keeps_the_file_so_the_session_is_not_lost()
    {
        var store = new InMemoryActiveSessionStore();
        var history = new FailingHistoryStore { FailSaves = true };
        await using var harness = await ServerHarness.StartAsync(history: history, activeSessions: store);
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 5, durationMs: 60_000));

        await client.CommandAsync(Msg.Finish());

        Assert.NotNull(store.Record);
    }

    [Fact]
    public async Task A_store_that_cannot_write_never_disturbs_the_session()
    {
        var store = new RefusingActiveSessionStore();
        await using var harness = await ServerHarness.StartAsync(activeSessions: store);
        var client = await harness.ConnectAsync();

        var start = await client.CommandAsync(Msg.Start(slides: 5, durationMs: 60_000));
        var next = await client.CommandAsync(Msg.Next());

        Assert.True(start.Payload.Ok);
        Assert.True(next.Payload.Ok);
        Assert.Contains(harness.Logs.Lines, l => l.Contains("could not be saved", StringComparison.Ordinal));
    }

    // ---- recovery ----

    [Fact]
    public async Task An_orphaned_session_is_saved_once_as_interrupted_with_the_last_written_time_and_is_not_resumed()
    {
        var store = new InMemoryActiveSessionStore { Record = Orphan(elapsedMs: 125_000) };

        await using var harness = await ServerHarness.StartAsync(activeSessions: store);

        var record = Assert.Single(harness.Memory.Records);
        Assert.Equal("Orphan", record.Name);
        Assert.Equal(FinalState.InterruptedByDisconnection, record.FinalState);
        Assert.Equal(125_000, record.ActualDurationMs);
        Assert.Equal(900_000, record.PlannedDurationMs);
        Assert.Equal(20, record.PlannedSlides);
        Assert.Equal(7, record.RegisteredSlides);
        Assert.Equal(Orphan().SavedAtUtc, record.FinishedAtUtc);
        Assert.True(record.EndedWithinPlannedTime);
        Assert.Null(store.Record);
        Assert.Null(harness.Service.Status.Session);
        Assert.Single(harness.Activities, a => a.Kind == ActivityKind.SessionRecovered);
    }

    [Fact]
    public async Task A_paused_orphan_is_recovered_the_same_way()
    {
        var store = new InMemoryActiveSessionStore { Record = Orphan(paused: true) };

        await using var harness = await ServerHarness.StartAsync(activeSessions: store);

        Assert.Single(harness.Memory.Records);
        Assert.Null(store.Record);
    }

    [Fact]
    public async Task A_second_start_after_a_normal_recovery_adds_nothing()
    {
        var store = new InMemoryActiveSessionStore { Record = Orphan() };
        var history = new InMemoryHistoryStore();
        await using (await ServerHarness.StartAsync(history: history, activeSessions: store))
        {
        }

        await using var again = await ServerHarness.StartAsync(history: history, activeSessions: store);

        Assert.Single(history.Records);
        Assert.Empty(again.Activities);
    }

    [Fact]
    public async Task A_crash_during_recovery_never_creates_a_duplicate()
    {
        // First run: the record is saved but the file cannot be removed (the process dies right there).
        var store = new StickyActiveSessionStore { Record = Orphan() };
        var history = new InMemoryHistoryStore();
        await using (await ServerHarness.StartAsync(history: history, activeSessions: store))
        {
        }

        Assert.Single(history.Records);
        Assert.NotNull(store.Record); // still there, as after a crash

        // Second run: the same record is found in the history, so only the file is removed.
        store.Sticky = false;
        await using var second = await ServerHarness.StartAsync(history: history, activeSessions: store);

        Assert.Single(history.Records);
        Assert.Null(store.Record);
    }

    [Fact]
    public async Task A_history_that_fails_during_recovery_keeps_the_file_and_the_next_start_recovers_it()
    {
        var store = new InMemoryActiveSessionStore { Record = Orphan() };
        var history = new FailingHistoryStore { FailSaves = true };
        await using (var first = await ServerHarness.StartAsync(history: history, activeSessions: store))
        {
            Assert.Empty(first.Activities);
        }

        Assert.NotNull(store.Record);
        Assert.Empty(history.Inner.Records);

        history.FailSaves = false;
        await using var second = await ServerHarness.StartAsync(history: history, activeSessions: store);

        Assert.Single(history.Inner.Records);
        Assert.Null(store.Record);
    }

    [Fact]
    public async Task A_corrupt_file_never_crashes_startup_and_is_discarded_without_a_record()
    {
        var store = new InMemoryActiveSessionStore { Corrupt = true };

        await using var harness = await ServerHarness.StartAsync(activeSessions: store);

        Assert.Empty(harness.Memory.Records);
        Assert.False(store.Corrupt);
        Assert.Empty(harness.Activities);
        Assert.True(harness.Host.IsRunning);
    }

    [Fact]
    public async Task Recovery_never_replaces_a_session_that_is_already_running()
    {
        var store = new InMemoryActiveSessionStore();
        await using var harness = await ServerHarness.StartAsync(activeSessions: store);
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 5, durationMs: 60_000));
        var written = store.Record;

        var recovered = await harness.Service.RecoverInterruptedSessionAsync();

        Assert.False(recovered);
        Assert.Equal(written, store.Record);
        Assert.Empty(harness.Memory.Records);
    }

    [Fact]
    public async Task Recovery_with_real_files_and_the_real_database_gives_exactly_one_record_even_if_run_twice()
    {
        var sessionFile = TempFile("active-session.json");
        var databaseFile = TempFile("history.db");
        try
        {
            var files = new JsonActiveSessionStore(sessionFile);
            Assert.True(files.Save(Orphan(elapsedMs: 61_000)));
            var database = await SqliteHistoryStore.OpenAsync(databaseFile);

            await using (await ServerHarness.StartAsync(history: database, activeSessions: files))
            {
            }

            // The file was removed; put the very same record back, as if the removal had been lost in a crash.
            Assert.False(File.Exists(sessionFile));
            Assert.True(files.Save(Orphan(elapsedMs: 61_000)));
            await using (await ServerHarness.StartAsync(history: database, activeSessions: files))
            {
            }

            var all = await database.ListNewestFirstAsync(10);
            var only = Assert.Single(all);
            Assert.Equal(61_000, only.ActualDurationMs);
            Assert.Equal(FinalState.InterruptedByDisconnection, only.FinalState);
            Assert.False(File.Exists(sessionFile));
        }
        finally
        {
            DeleteFolderOf(sessionFile);
            DeleteFolderOf(databaseFile);
        }
    }

    // ---- the file store itself ----

    [Fact]
    public void The_file_store_round_trips_a_record_and_leaves_no_temporary_file()
    {
        var path = TempFile("active-session.json");
        try
        {
            var store = new JsonActiveSessionStore(path);

            Assert.Equal(ActiveSessionLoadStatus.None, store.Load().Status);
            Assert.True(store.Save(Orphan(paused: true)));
            Assert.True(store.Save(Orphan(elapsedMs: 200_000, paused: true))); // replaces the file

            var load = store.Load();
            Assert.Equal(ActiveSessionLoadStatus.Valid, load.Status);
            Assert.Equal(Orphan(elapsedMs: 200_000, paused: true), load.Record);
            Assert.False(File.Exists(path + ".tmp"));
            Assert.True(store.Delete());
            Assert.True(store.Delete()); // deleting nothing is fine
            Assert.Equal(ActiveSessionLoadStatus.None, store.Load().Status);
        }
        finally
        {
            DeleteFolderOf(path);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"version\":1,\"name\":\"x\",\"plannedSl")] // cut in the middle, as by a power cut
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"version\":2,\"name\":\"x\",\"plannedSlides\":5,\"plannedDurationMs\":60000,\"currentSlide\":1,\"elapsedMs\":0,\"state\":\"running\",\"savedAtUnixMs\":1}")]
    [InlineData("{\"version\":1,\"name\":\"\",\"plannedSlides\":5,\"plannedDurationMs\":60000,\"currentSlide\":1,\"elapsedMs\":0,\"state\":\"running\",\"savedAtUnixMs\":1}")]
    [InlineData("{\"version\":1,\"name\":\"x\",\"plannedSlides\":5,\"plannedDurationMs\":60000,\"currentSlide\":6,\"elapsedMs\":0,\"state\":\"running\",\"savedAtUnixMs\":1}")]
    [InlineData("{\"version\":1,\"name\":\"x\",\"plannedSlides\":5,\"plannedDurationMs\":60000,\"currentSlide\":0,\"elapsedMs\":0,\"state\":\"running\",\"savedAtUnixMs\":1}")]
    [InlineData("{\"version\":1,\"name\":\"x\",\"plannedSlides\":5,\"plannedDurationMs\":60000,\"currentSlide\":1,\"elapsedMs\":-5,\"state\":\"running\",\"savedAtUnixMs\":1}")]
    [InlineData("{\"version\":1,\"name\":\"x\",\"plannedSlides\":5,\"plannedDurationMs\":60000,\"currentSlide\":1,\"elapsedMs\":0,\"state\":\"finished\",\"savedAtUnixMs\":1}")]
    [InlineData("{\"version\":1,\"name\":\"x\",\"plannedSlides\":5,\"plannedDurationMs\":10,\"currentSlide\":1,\"elapsedMs\":0,\"state\":\"running\",\"savedAtUnixMs\":1}")]
    [InlineData("{\"version\":1,\"name\":\"x\",\"plannedSlides\":\"five\",\"plannedDurationMs\":60000,\"currentSlide\":1,\"elapsedMs\":0,\"state\":\"running\",\"savedAtUnixMs\":1}")]
    [InlineData("{\"version\":1,\"name\":\"x\",\"plannedSlides\":5,\"plannedDurationMs\":60000,\"currentSlide\":1,\"elapsedMs\":0,\"state\":\"running\",\"savedAtUnixMs\":99999999999999999}")]
    public void Corrupt_partial_or_out_of_range_files_are_reported_invalid_and_never_throw(string content)
    {
        var path = TempFile("active-session.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);

            var load = new JsonActiveSessionStore(path).Load();

            Assert.Equal(ActiveSessionLoadStatus.Invalid, load.Status);
            Assert.Null(load.Record);
        }
        finally
        {
            DeleteFolderOf(path);
        }
    }

    [Fact]
    public void A_file_with_invalid_bytes_is_invalid_not_an_exception()
    {
        var path = TempFile("active-session.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[] { 0xFF, 0xFE, 0x00, 0xC3, 0x28, 0x7B });

            Assert.Equal(ActiveSessionLoadStatus.Invalid, new JsonActiveSessionStore(path).Load().Status);
        }
        finally
        {
            DeleteFolderOf(path);
        }
    }

    [Fact]
    public void A_path_that_cannot_be_written_returns_false_instead_of_throwing()
    {
        var folder = Path.Combine(Path.GetTempPath(), "presenta-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var blocker = Path.Combine(folder, "blocker");
            File.WriteAllText(blocker, "a file where a folder is needed");

            var store = new JsonActiveSessionStore(Path.Combine(blocker, "active-session.json"));

            Assert.False(store.Save(Orphan()));
            Assert.Equal(ActiveSessionLoadStatus.None, store.Load().Status);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_file_never_contains_the_pairing_code_or_anything_but_the_session()
    {
        var path = TempFile("active-session.json");
        try
        {
            new JsonActiveSessionStore(path).Save(Orphan());

            var text = File.ReadAllText(path);

            Assert.DoesNotContain("pairing", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("code", text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteFolderOf(path);
        }
    }

    [Fact]
    public void The_recovered_summary_is_always_the_same_for_the_same_record()
    {
        var a = ActiveSessionRecovery.BuildInterruptedSummary(Orphan(elapsedMs: 1_000_000));
        var b = ActiveSessionRecovery.BuildInterruptedSummary(Orphan(elapsedMs: 1_000_000));

        Assert.Equal(a, b);
        Assert.False(a.EndedWithinPlannedTime); // 1,000 s over a 900 s plan
    }

    private static void DeleteFolderOf(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (folder is not null && Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ---- test doubles ----

    private sealed class FailingHistoryStore : IHistoryStore
    {
        public InMemoryHistoryStore Inner { get; } = new();

        public bool FailSaves { get; set; }

        public Task SaveAsync(SessionSummary summary, CancellationToken cancellationToken = default) =>
            FailSaves ? throw new IOException("The disk is full.") : Inner.SaveAsync(summary, cancellationToken);

        public Task<IReadOnlyList<SessionSummary>> ListNewestFirstAsync(int limit, CancellationToken cancellationToken = default) =>
            Inner.ListNewestFirstAsync(limit, cancellationToken);
    }

    private sealed class RefusingActiveSessionStore : IActiveSessionStore
    {
        public bool Save(ActiveSessionRecord record) => false;

        public ActiveSessionLoad Load() => new(ActiveSessionLoadStatus.None, null);

        public bool Delete() => true;
    }

    private sealed class StickyActiveSessionStore : InMemoryActiveSessionStore
    {
        public bool Sticky { get; set; } = true;

        public override bool Delete() => !Sticky && base.Delete();
    }
}
