using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Presenta.Domain.History;
using Presenta.TestSupport;

namespace Presenta.Server.Tests.Support;

/// <summary>Captures log output so tests can check that nothing sensitive is logged.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _lines;

        public CapturingLogger(ConcurrentQueue<string> lines) => _lines = lines;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _lines.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
        }
    }
}

/// <summary>
/// A companion host on an ephemeral loopback port with a fake clock, a fake keyboard and an in-memory (or supplied)
/// history store. Connect watches with <see cref="ConnectAsync"/>.
/// </summary>
internal sealed class ServerHarness : IAsyncDisposable
{
    public const string Code = "123456";

    private readonly List<TestClient> _clients = new();

    private ServerHarness(CompanionHost host, ManualTimeProvider time, RecordingSlideKeyboard keyboard, IHistoryStore history, CapturingLoggerProvider logs)
    {
        Host = host;
        Time = time;
        Keyboard = keyboard;
        History = history;
        Logs = logs;
    }

    public CompanionHost Host { get; }

    public CompanionService Service => Host.Service;

    public ManualTimeProvider Time { get; }

    public RecordingSlideKeyboard Keyboard { get; }

    public IHistoryStore History { get; }

    public InMemoryHistoryStore Memory => (InMemoryHistoryStore)History;

    public CapturingLoggerProvider Logs { get; }

    public int Port => Host.Port;

    public static async Task<ServerHarness> StartAsync(
        Func<CompanionServerOptions, CompanionServerOptions>? configure = null,
        IHistoryStore? history = null,
        ManualTimeProvider? time = null)
    {
        var options = new CompanionServerOptions { Port = 0, PairingCode = Code };
        if (configure is not null)
        {
            options = configure(options);
        }

        var logs = new CapturingLoggerProvider();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        var clock = time ?? new ManualTimeProvider();
        var keyboard = new RecordingSlideKeyboard();
        var store = history ?? new InMemoryHistoryStore();
        var host = new CompanionHost(options, keyboard, store, clock, loggerFactory);
        await host.StartAsync();
        return new ServerHarness(host, clock, keyboard, store, logs);
    }

    /// <summary>Opens a WebSocket and (by default) pairs it with the correct code.</summary>
    public async Task<TestClient> ConnectAsync(bool pair = true)
    {
        var client = await TestClient.ConnectAsync(Port);
        _clients.Add(client);
        if (pair)
        {
            await client.PairAsync(Code);
        }

        return client;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        await Host.DisposeAsync();
    }
}

/// <summary>Polls a condition for up to a few seconds (real time), for state that changes on the server without a message.</summary>
internal static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, string because)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(8))
            {
                Assert.Fail("Timed out waiting for: " + because);
            }

            await Task.Delay(10);
        }
    }
}
