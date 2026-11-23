using Presenta.Protocol;
using Presenta.Server.Tests.Support;

namespace Presenta.Server.Tests;

/// <summary>Phase 8, item 3: the optional remembered pairing code (ADR-69).</summary>
public class RememberedPairingCodeTests
{
    private static string TempFile(string name) =>
        Path.Combine(Path.GetTempPath(), "presenta-tests-" + Guid.NewGuid().ToString("N"), name);

    private static Func<CompanionServerOptions, CompanionServerOptions> RandomCode => o => o with { PairingCode = null };

    private static void DeleteFolderOf(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (folder is not null && Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task By_default_nothing_is_remembered_and_nothing_is_stored()
    {
        var settings = new InMemoryCompanionSettingsStore();

        await using var harness = await ServerHarness.StartAsync(RandomCode, settings: settings);

        Assert.False(harness.Service.RememberPairingCode);
        Assert.Equal(CompanionSettings.Default, settings.Current);
        Assert.Matches("^[0-9]{6}$", harness.Service.PairingCode);
    }

    [Fact]
    public async Task Turning_it_on_stores_the_current_code_and_a_restart_keeps_it()
    {
        var settings = new InMemoryCompanionSettingsStore();
        string code;
        await using (var first = await ServerHarness.StartAsync(RandomCode, settings: settings))
        {
            first.Service.SetRememberPairingCode(true);
            code = first.Service.PairingCode;
            Assert.Equal(new CompanionSettings(true, code), settings.Current);
        }

        await using var second = await ServerHarness.StartAsync(RandomCode, settings: settings);

        Assert.Equal(code, second.Service.PairingCode);
        Assert.True(second.Service.RememberPairingCode);
    }

    [Fact]
    public async Task Without_the_option_every_start_gets_a_new_code()
    {
        var settings = new InMemoryCompanionSettingsStore();
        var codes = new HashSet<string>();
        for (var run = 0; run < 6; run++)
        {
            await using var harness = await ServerHarness.StartAsync(RandomCode, settings: settings);
            codes.Add(harness.Service.PairingCode);
        }

        Assert.True(codes.Count > 1, "six random six-digit codes were all equal");
    }

    [Fact]
    public async Task Regenerating_replaces_the_stored_code_when_remembered_and_stores_nothing_otherwise()
    {
        var remembered = new InMemoryCompanionSettingsStore(new CompanionSettings(true, "111111"));
        await using (var harness = await ServerHarness.StartAsync(RandomCode, settings: remembered))
        {
            Assert.Equal("111111", harness.Service.PairingCode);

            harness.Service.RegeneratePairingCode();

            Assert.NotEqual("111111", harness.Service.PairingCode);
            Assert.Equal(new CompanionSettings(true, harness.Service.PairingCode), remembered.Current);
        }

        var notRemembered = new InMemoryCompanionSettingsStore();
        await using (var harness = await ServerHarness.StartAsync(RandomCode, settings: notRemembered))
        {
            harness.Service.RegeneratePairingCode();

            Assert.Equal(CompanionSettings.Default, notRemembered.Current);
        }
    }

    [Fact]
    public async Task Turning_it_off_removes_the_stored_code()
    {
        var settings = new InMemoryCompanionSettingsStore(new CompanionSettings(true, "222222"));
        await using var harness = await ServerHarness.StartAsync(RandomCode, settings: settings);

        harness.Service.SetRememberPairingCode(false);

        Assert.Equal(new CompanionSettings(false, null), settings.Current);
        Assert.False(harness.Service.RememberPairingCode);
        Assert.Equal("222222", harness.Service.PairingCode); // the code in use does not change by itself
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12ab56")]
    public async Task An_unusable_stored_code_is_replaced_by_a_valid_new_one_that_is_stored(string? stored)
    {
        var settings = new InMemoryCompanionSettingsStore(new CompanionSettings(true, stored));

        await using var harness = await ServerHarness.StartAsync(RandomCode, settings: settings);

        Assert.Matches("^[0-9]{6}$", harness.Service.PairingCode);
        Assert.Equal(new CompanionSettings(true, harness.Service.PairingCode), settings.Current);
    }

    [Fact]
    public async Task A_fixed_code_from_the_options_wins_over_the_stored_one()
    {
        var settings = new InMemoryCompanionSettingsStore(new CompanionSettings(true, "333333"));

        await using var harness = await ServerHarness.StartAsync(settings: settings);

        Assert.Equal(ServerHarness.Code, harness.Service.PairingCode);
    }

    [Fact]
    public async Task The_remembered_code_pairs_a_watch_after_a_restart_and_the_old_one_stops_working_after_regenerating()
    {
        var settings = new InMemoryCompanionSettingsStore();
        string code;
        await using (var first = await ServerHarness.StartAsync(RandomCode, settings: settings))
        {
            first.Service.SetRememberPairingCode(true);
            code = first.Service.PairingCode;
            var watch = await first.ConnectAsync(pair: false);
            await watch.PairAsync(code);
        }

        await using var second = await ServerHarness.StartAsync(RandomCode, settings: settings);
        var again = await second.ConnectAsync(pair: false);
        await again.PairAsync(code);
        await Wait.UntilAsync(() => second.Service.Status.WatchConnected, "the watch to reconnect with the stored code");

        second.Service.RegeneratePairingCode();
        var late = await second.ConnectAsync(pair: false);
        await late.SendAsync(Msg.Hello(code));
        var rejected = await late.ReceiveAsync<HelloRejectedMessage>();

        Assert.Equal(ErrorCode.InvalidPairingCode, rejected.Payload.Reason);
        Assert.Equal(second.Service.PairingCode, settings.Current.PairingCode);
    }

    [Fact]
    public async Task Lockout_still_applies_to_a_remembered_code()
    {
        var settings = new InMemoryCompanionSettingsStore(new CompanionSettings(true, "444444"));
        await using var harness = await ServerHarness.StartAsync(RandomCode, settings: settings);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await harness.ConnectAsync(pair: false);
            await wrong.SendAsync(Msg.Hello("000000"));
            Assert.Equal(ErrorCode.InvalidPairingCode, (await wrong.ReceiveAsync<HelloRejectedMessage>()).Payload.Reason);
        }

        var locked = await harness.ConnectAsync(pair: false);
        await locked.SendAsync(Msg.Hello("444444")); // even the right code waits while the address is locked out
        var rejected = await locked.ReceiveAsync<HelloRejectedMessage>();

        Assert.Equal(ErrorCode.PairingLockedOut, rejected.Payload.Reason);
    }

    [Fact]
    public async Task The_code_is_never_written_to_the_log()
    {
        var settings = new InMemoryCompanionSettingsStore();
        await using var harness = await ServerHarness.StartAsync(RandomCode, settings: settings);
        harness.Service.SetRememberPairingCode(true);
        var first = harness.Service.PairingCode;
        harness.Service.RegeneratePairingCode();
        var second = harness.Service.PairingCode;
        var watch = await harness.ConnectAsync(pair: false);
        await watch.SendAsync(Msg.Hello("999999"));
        await watch.ReceiveAsync<HelloRejectedMessage>();

        Assert.DoesNotContain(harness.Logs.Lines, line => line.Contains(first, StringComparison.Ordinal) || line.Contains(second, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_settings_store_that_cannot_write_only_logs_and_the_server_keeps_working()
    {
        var settings = new RefusingSettingsStore();
        await using var harness = await ServerHarness.StartAsync(RandomCode, settings: settings);

        harness.Service.SetRememberPairingCode(true);
        harness.Service.RegeneratePairingCode();

        Assert.True(harness.Host.IsRunning);
        Assert.Contains(harness.Logs.Lines, l => l.Contains("settings file could not be written", StringComparison.Ordinal));
    }

    // ---- the file store ----

    [Fact]
    public void The_file_store_round_trips_and_turning_it_off_removes_the_code_from_the_file()
    {
        var path = TempFile("settings.json");
        try
        {
            var store = new JsonCompanionSettingsStore(path);
            Assert.Equal(CompanionSettings.Default, store.Load());

            Assert.True(store.Save(new CompanionSettings(true, "123456")));
            Assert.Equal(new CompanionSettings(true, "123456"), store.Load());
            Assert.Contains("123456", File.ReadAllText(path), StringComparison.Ordinal);

            Assert.True(store.Save(new CompanionSettings(false, "123456")));
            Assert.Equal(CompanionSettings.Default, store.Load());
            Assert.DoesNotContain("123456", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            DeleteFolderOf(path);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("{\"version\":1,\"rememberPairingCode\":tr")] // cut in the middle
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"rememberPairingCode\":\"yes\",\"pairingCode\":\"123456\"}")]
    [InlineData("{\"rememberPairingCode\":false,\"pairingCode\":\"123456\"}")]
    public void Corrupt_or_off_settings_fall_back_to_the_defaults_without_a_code(string content)
    {
        var path = TempFile("settings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);

            Assert.Equal(CompanionSettings.Default, new JsonCompanionSettingsStore(path).Load());
        }
        finally
        {
            DeleteFolderOf(path);
        }
    }

    [Theory]
    [InlineData("{\"rememberPairingCode\":true}")]
    [InlineData("{\"rememberPairingCode\":true,\"pairingCode\":12}")]
    [InlineData("{\"rememberPairingCode\":true,\"pairingCode\":\"12\"}")]
    [InlineData("{\"rememberPairingCode\":true,\"pairingCode\":\"abcdef\"}")]
    public void A_remember_flag_with_an_unusable_code_loads_as_remember_without_a_code(string content)
    {
        var path = TempFile("settings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);

            Assert.Equal(new CompanionSettings(true, null), new JsonCompanionSettingsStore(path).Load());
        }
        finally
        {
            DeleteFolderOf(path);
        }
    }

    [Fact]
    public async Task A_corrupt_settings_file_does_not_stop_the_server_from_starting_with_a_random_code()
    {
        var path = TempFile("settings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{{{ definitely not json");

            await using var harness = await ServerHarness.StartAsync(RandomCode, settings: new JsonCompanionSettingsStore(path));

            Assert.True(harness.Host.IsRunning);
            Assert.False(harness.Service.RememberPairingCode);
            Assert.Matches("^[0-9]{6}$", harness.Service.PairingCode);
        }
        finally
        {
            DeleteFolderOf(path);
        }
    }

    [Fact]
    public async Task With_real_files_a_restart_keeps_the_code_and_regenerating_replaces_the_file_content()
    {
        var path = TempFile("settings.json");
        try
        {
            string code;
            await using (var first = await ServerHarness.StartAsync(RandomCode, settings: new JsonCompanionSettingsStore(path)))
            {
                first.Service.SetRememberPairingCode(true);
                code = first.Service.PairingCode;
            }

            await using (var second = await ServerHarness.StartAsync(RandomCode, settings: new JsonCompanionSettingsStore(path)))
            {
                Assert.Equal(code, second.Service.PairingCode);
                second.Service.RegeneratePairingCode();
                Assert.NotEqual(code, second.Service.PairingCode);
            }

            var stored = new JsonCompanionSettingsStore(path).Load();
            Assert.True(stored.RememberPairingCode);
            Assert.NotEqual(code, stored.PairingCode);
        }
        finally
        {
            DeleteFolderOf(path);
        }
    }

    private sealed class RefusingSettingsStore : ICompanionSettingsStore
    {
        public CompanionSettings Load() => CompanionSettings.Default;

        public bool Save(CompanionSettings settings) => false;
    }
}
