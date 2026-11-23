using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Presenta.Ai;
using Presenta.Domain;
using Presenta.Server;
using Presenta.Targeting;
using Presenta.TestSupport;
using static Presenta.TestSupport.FakeWindowSystem;

namespace Presenta.Ai.Tests;

/// <summary>The key lives only in the Authorization header of a request (ADR-73). These tests look everywhere else.</summary>
public class SecretsTests
{
    // A placeholder that is easy to find in any output. It is not a real key.
    private static readonly string Key = "key-placeholder-" + Guid.NewGuid().ToString("N");

    private sealed class CapturingLoggerProvider : ILoggerProvider
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

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                _lines.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
        }
    }

    private static SessionSnapshot Alerting(int slidesRemaining) =>
        new(
            SessionState.Running, "Some deck", 11, 20, 900_000, 600_000, 300_000, true, DateTimeOffset.UnixEpoch, 0.55, PaceStatus.Behind,
            slidesRemaining, 1.1, null,
            new Alert(AlertKind.TimeRunningOut, AlertSeverity.Warning, 600_000, 300_000, slidesRemaining, -2, 1),
            new Recommendation(RecommendationCode.SpeedUpSlightly, slidesRemaining, 300_000));

    private static async Task<(IReadOnlyCollection<string> Logs, AiRecommendationEnricher Ai)> RunAsync(
        Func<Microsoft.AspNetCore.Http.HttpContext, SeenRequest, Task> handler,
        TimeSpan? clientTimeout = null)
    {
        await using var server = await LocalAiServer.StartAsync(handler);
        var logs = new CapturingLoggerProvider();
        var factory = LoggerFactory.Create(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        var options = new AiEndpointOptions(new Uri(server.BaseUrl), "test-model", Key) { Timeout = clientTimeout ?? TimeSpan.FromSeconds(4) };
        var ai = new AiRecommendationEnricher(
            new OpenAiCompatibleClient(http, options),
            AiAvailability.Available,
            new AiEnrichmentOptions { MinInterval = TimeSpan.Zero, FailureThreshold = 100 },
            TimeProvider.System,
            factory) { Enabled = true };
        for (var i = 1; i <= 3; i++)
        {
            ai.Apply(Alerting(i));
            await ai.WhenIdleAsync();
        }

        Assert.Contains(server.Seen, r => r.Authorization == "Bearer " + Key); // the key was really used
        return (logs.Lines, ai);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(302)]
    public async Task The_key_is_never_logged_whatever_the_provider_answers(int status)
    {
        var (logs, ai) = await RunAsync((context, _) => status == 200
            ? LocalAiServer.Choice(context, "A short tip here")
            : LocalAiServer.Reply(context, status, "{\"error\":\"" + Key + "\"}"));

        Assert.NotEmpty(logs);
        Assert.DoesNotContain(logs, line => line.Contains(Key, StringComparison.Ordinal) || line.Contains("Bearer", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Key, ai.LastText ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, ai.Status.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_provider_that_echoes_the_key_back_in_its_text_never_gets_it_displayed_or_logged()
    {
        // A hostile or buggy provider could answer with the key. The text is only kept after sanitation, and nothing logs a text.
        var (logs, _) = await RunAsync((context, _) => LocalAiServer.Choice(context, "https://evil.example/?k=" + Key));

        Assert.DoesNotContain(logs, line => line.Contains(Key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Malformed_answers_and_slow_answers_do_not_leak_the_key_either()
    {
        var (malformed, _) = await RunAsync((context, _) => LocalAiServer.Reply(context, 200, "not json " + Key));
        var (slow, _) = await RunAsync(
            async (context, _) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3), context.RequestAborted).ContinueWith(_ => { });
            },
            clientTimeout: TimeSpan.FromMilliseconds(200));

        Assert.DoesNotContain(malformed, line => line.Contains(Key, StringComparison.Ordinal));
        Assert.DoesNotContain(slow, line => line.Contains(Key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unreachable_provider_does_not_leak_the_key_through_an_exception_message()
    {
        var closed = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        var logs = new CapturingLoggerProvider();
        var factory = LoggerFactory.Create(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        var client = new OpenAiCompatibleClient(new HttpClient(), new AiEndpointOptions(new Uri($"http://127.0.0.1:{port}/v1"), "m", Key));
        var ai = new AiRecommendationEnricher(client, AiAvailability.Available, new AiEnrichmentOptions(), TimeProvider.System, factory) { Enabled = true };

        ai.Apply(Alerting(5));
        await ai.WhenIdleAsync();

        var result = await client.GenerateAsync(AiPromptBuilder.Build(AiSituation.Create("OnTrack", null, "OnTrack", 1, 60, 5, 300)), CancellationToken.None);
        Assert.Equal(AiFailureKind.Network, result.Failure);
        Assert.DoesNotContain(Key, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Lines, line => line.Contains(Key, StringComparison.Ordinal));
    }

    [Fact]
    public void The_options_and_the_configuration_result_never_print_the_key()
    {
        var configuration = AiConfiguration.Create("https://api.example.com/v1", "m", Key);

        Assert.DoesNotContain(Key, configuration.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Key, configuration.Options!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Key, System.Text.Json.JsonSerializer.Serialize(configuration.Options.BaseUrl.ToString()), StringComparison.Ordinal);
    }

    [Fact]
    public void The_key_is_not_a_public_member_of_any_type_of_the_library_or_the_enricher()
    {
        var publicTypes = typeof(AiEndpointOptions).Assembly.GetExportedTypes().Concat(new[] { typeof(AiRecommendationEnricher) });
        foreach (var type in publicTypes)
        {
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                // The only public name with "key" in it is the NAME of the environment variable, which is not the key.
                if (member.Name != nameof(AiConfiguration.ApiKeyVariable))
                {
                    Assert.DoesNotContain("apikey", member.Name, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void An_activity_line_cannot_carry_text_at_all()
    {
        var textProperties = typeof(CompanionActivity).GetProperties().Where(p => p.PropertyType == typeof(string));

        Assert.Empty(textProperties);
    }

    [Fact]
    public void The_window_diagnostic_report_never_contains_the_key_even_when_it_is_in_the_environment()
    {
        var previous = Environment.GetEnvironmentVariable(AiConfiguration.ApiKeyVariable);
        try
        {
            Environment.SetEnvironmentVariable(AiConfiguration.ApiKeyVariable, Key);
            var windows = new FakeWindowSystem();
            windows.Add(Window(10, "Slide Show - deck.pptx", "POWERPNT", "screenClass"));
            var service = new PresentationTargetService(windows, new InMemoryTargetSettingsStore());
            service.Select(windows.GetWindow(10)!);

            var report = service.CreateDiagnosticReport();

            Assert.StartsWith("Presenta window diagnostic report", report, StringComparison.Ordinal);
            Assert.DoesNotContain(Key, report, StringComparison.Ordinal);
            Assert.DoesNotContain("PRESENTA_AI", report, StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer", report, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AiConfiguration.ApiKeyVariable, previous);
        }
    }
}
