using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Presenta.Ai.Tests;

/// <summary>One request as the local test server saw it.</summary>
internal sealed record SeenRequest(string Method, string Path, string? Authorization, string? ContentType, string Body);

/// <summary>A provider stand-in on an ephemeral loopback port. Tests decide the answer. No real network is ever used.</summary>
internal sealed class LocalAiServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<SeenRequest> _seen;

    private LocalAiServer(WebApplication app, int port, ConcurrentQueue<SeenRequest> seen)
    {
        _app = app;
        _seen = seen;
        Port = port;
    }

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}/v1";

    /// <summary>Every request received so far, oldest first.</summary>
    public IReadOnlyList<SeenRequest> Seen => _seen.ToArray();

    public static async Task<LocalAiServer> StartAsync(Func<HttpContext, SeenRequest, Task> handler)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var queue = new ConcurrentQueue<SeenRequest>();
        app.Run(async context =>
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var authorization = context.Request.Headers.Authorization.ToString();
            var seen = new SeenRequest(
                context.Request.Method,
                context.Request.Path.Value ?? string.Empty,
                authorization.Length > 0 ? authorization : null,
                context.Request.ContentType,
                await reader.ReadToEndAsync());
            queue.Enqueue(seen);
            await handler(context, seen);
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new LocalAiServer(app, new Uri(address).Port, queue);
    }

    public static Task Reply(HttpContext context, int status, string body, string contentType = "application/json")
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        return context.Response.WriteAsync(body);
    }

    public static Task Choice(HttpContext context, string content) =>
        Reply(context, 200, "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + System.Text.Json.JsonSerializer.Serialize(content) + "}}]}");

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class ClientTests
{
    private const string Key = "test-key-0123456789";

    private static HttpClient NewHttp() => new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };

    private static OpenAiCompatibleClient ClientFor(LocalAiServer server, TimeSpan? timeout = null, string suffix = "") =>
        new(NewHttp(), new AiEndpointOptions(new Uri(server.BaseUrl + suffix), "test-model", Key) { Timeout = timeout ?? TimeSpan.FromSeconds(4) });

    private static AiPrompt Prompt() =>
        AiPromptBuilder.Build(AiSituation.Create("SpeedUpSlightly", "TimeRunningOut", "Behind", 9, 300, 20, 900));

    private static async Task<AiTextResult> AskAsync(Func<HttpContext, SeenRequest, Task> handler, TimeSpan? timeout = null)
    {
        await using var server = await LocalAiServer.StartAsync(handler);
        return await ClientFor(server, timeout).GenerateAsync(Prompt(), CancellationToken.None);
    }

    [Fact]
    public async Task A_good_answer_returns_the_text_and_the_request_has_the_pinned_shape()
    {
        await using var server = await LocalAiServer.StartAsync((context, _) => LocalAiServer.Choice(context, "Speed up a little"));

        var result = await ClientFor(server).GenerateAsync(Prompt(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Speed up a little", result.Text);
        var request = Assert.Single(server.Seen);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/v1/chat/completions", request.Path);
        Assert.Equal("Bearer " + Key, request.Authorization);
        Assert.StartsWith("application/json", request.ContentType, StringComparison.Ordinal);
        Assert.Equal(OpenAiCompatibleClient.BuildRequestJson("test-model", Prompt(), 32, 0.2), request.Body);
    }

    [Fact]
    public async Task A_base_address_with_a_trailing_slash_gives_the_same_endpoint()
    {
        await using var server = await LocalAiServer.StartAsync((context, _) => LocalAiServer.Choice(context, "Ok then"));

        await ClientFor(server, suffix: "/").GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal("/v1/chat/completions", Assert.Single(server.Seen).Path);
    }

    [Fact]
    public async Task The_key_travels_only_in_the_authorization_header_never_in_the_body_or_the_address()
    {
        await using var server = await LocalAiServer.StartAsync((context, _) => LocalAiServer.Choice(context, "Ok then"));

        await ClientFor(server).GenerateAsync(Prompt(), CancellationToken.None);

        var request = Assert.Single(server.Seen);
        Assert.DoesNotContain(Key, request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, request.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_body_holds_no_session_name_window_title_or_file_name()
    {
        await using var server = await LocalAiServer.StartAsync((context, _) => LocalAiServer.Choice(context, "Ok then"));
        // Everything the program could know about the user, none of which is ever given to the prompt builder.
        var canaries = new[] { "Quarterly", "secret", ".pptx", "PowerPoint", "POWERPNT", "Notepad", Environment.MachineName, Environment.UserName };

        await ClientFor(server).GenerateAsync(Prompt(), CancellationToken.None);

        var body = Assert.Single(server.Seen).Body;
        foreach (var canary in canaries.Where(c => c.Length >= 3))
        {
            Assert.DoesNotContain(canary, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(401, AiFailureKind.Unauthorized)]
    [InlineData(403, AiFailureKind.Unauthorized)]
    [InlineData(500, AiFailureKind.ServerError)]
    [InlineData(502, AiFailureKind.ServerError)]
    [InlineData(503, AiFailureKind.ServerError)]
    [InlineData(400, AiFailureKind.Rejected)]
    [InlineData(404, AiFailureKind.Rejected)]
    [InlineData(302, AiFailureKind.Rejected)]
    public async Task Error_statuses_map_to_their_failure_kind(int status, AiFailureKind expected)
    {
        var result = await AskAsync((context, _) => LocalAiServer.Reply(context, status, "{\"error\":\"nope\"}"));

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Failure);
        Assert.Null(result.Text);
    }

    [Fact]
    public async Task A_429_reports_the_retry_after_in_seconds_or_none()
    {
        var withHeader = await AskAsync((context, _) =>
        {
            context.Response.Headers.RetryAfter = "7";
            return LocalAiServer.Reply(context, 429, "{}");
        });
        var withoutHeader = await AskAsync((context, _) => LocalAiServer.Reply(context, 429, "{}"));

        Assert.Equal(AiFailureKind.RateLimited, withHeader.Failure);
        Assert.Equal(TimeSpan.FromSeconds(7), withHeader.RetryAfter);
        Assert.Equal(AiFailureKind.RateLimited, withoutHeader.Failure);
        Assert.Null(withoutHeader.RetryAfter);
    }

    [Fact]
    public async Task A_429_with_a_date_gives_a_wait_that_is_never_negative()
    {
        var past = await AskAsync((context, _) =>
        {
            context.Response.Headers.RetryAfter = "Wed, 21 Oct 2015 07:28:00 GMT";
            return LocalAiServer.Reply(context, 429, "{}");
        });

        Assert.Equal(AiFailureKind.RateLimited, past.Failure);
        Assert.Equal(TimeSpan.Zero, past.RetryAfter);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":\"x\"}")]
    [InlineData("{\"choices\":[{}]}")]
    [InlineData("{\"choices\":[{\"message\":{}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":null}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":12}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"\"}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"cut off")]
    public async Task Malformed_or_empty_answers_are_reported_as_malformed(string body)
    {
        var result = await AskAsync((context, _) => LocalAiServer.Reply(context, 200, body));

        Assert.Equal(AiFailureKind.Malformed, result.Failure);
    }

    [Fact]
    public async Task An_answer_that_is_too_large_or_not_utf8_is_malformed()
    {
        var huge = await AskAsync((context, _) => LocalAiServer.Reply(context, 200, new string('x', AiLimits.MaxResponseBytes + 10)));
        var notUtf8 = await AskAsync(async (context, _) =>
        {
            context.Response.StatusCode = 200;
            await context.Response.Body.WriteAsync(new byte[] { 0xFF, 0xFE, 0xC3, 0x28 });
        });

        Assert.Equal(AiFailureKind.Malformed, huge.Failure);
        Assert.Equal(AiFailureKind.Malformed, notUtf8.Failure);
    }

    [Fact]
    public async Task A_slow_answer_beyond_the_timeout_is_a_timeout()
    {
        var result = await AskAsync(
            async (context, _) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(4), context.RequestAborted).ContinueWith(_ => { });
                await LocalAiServer.Choice(context, "too late");
            },
            timeout: TimeSpan.FromMilliseconds(300));

        Assert.Equal(AiFailureKind.Timeout, result.Failure);
    }

    [Fact]
    public async Task A_port_with_nothing_listening_is_a_network_failure()
    {
        var closedPort = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        closedPort.Start();
        var port = ((IPEndPoint)closedPort.LocalEndpoint).Port;
        closedPort.Stop();
        var client = new OpenAiCompatibleClient(NewHttp(), new AiEndpointOptions(new Uri($"http://127.0.0.1:{port}/v1"), "m", Key));

        var result = await client.GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal(AiFailureKind.Network, result.Failure);
    }

    [Fact]
    public async Task The_callers_cancellation_is_not_reported_as_a_timeout()
    {
        await using var server = await LocalAiServer.StartAsync(async (context, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(4), context.RequestAborted).ContinueWith(_ => { });
        });
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClientFor(server).GenerateAsync(Prompt(), cancel.Token));
    }

    [Fact]
    public async Task A_redirect_is_not_followed_so_the_key_is_never_sent_to_another_address()
    {
        await using var target = await LocalAiServer.StartAsync((context, _) => LocalAiServer.Choice(context, "elsewhere"));
        await using var origin = await LocalAiServer.StartAsync((context, _) =>
        {
            context.Response.Headers.Location = target.BaseUrl + "/chat/completions";
            return LocalAiServer.Reply(context, 307, string.Empty);
        });

        var result = await ClientFor(origin).GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal(AiFailureKind.Rejected, result.Failure);
        Assert.Empty(target.Seen);
    }
}

public class ConfigurationTests
{
    [Theory]
    [InlineData("https://api.example.com/v1")]
    [InlineData("https://api.example.com")]
    [InlineData("http://127.0.0.1:5000/v1")]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://[::1]:8080/v1")]
    [InlineData("HTTP://LOCALHOST/v1")]
    public void Https_or_a_loopback_address_is_accepted(string address)
    {
        var result = AiConfiguration.Create(address, "model", "key");

        Assert.Equal(AiAvailability.Available, result.Availability);
        Assert.NotNull(result.Options);
    }

    [Theory]
    [InlineData("http://api.example.com/v1")]
    [InlineData("http://192.168.1.20:8000/v1")]
    [InlineData("http://10.0.0.5/v1")]
    [InlineData("http://example.com")]
    public void Plain_http_to_another_computer_is_refused(string address)
    {
        var result = AiConfiguration.Create(address, "model", "key");

        Assert.Equal(AiAvailability.InsecureAddress, result.Availability);
        Assert.Null(result.Options);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/v1")]
    [InlineData("/relative/path")]
    [InlineData("https://user:secret@example.com/v1")]
    [InlineData("https://example.com/v1?x=1")]
    [InlineData("https://example.com/v1#frag")]
    public void A_bad_address_is_invalid(string address)
    {
        var result = AiConfiguration.Create(address, "model", "key");

        Assert.Equal(AiAvailability.InvalidAddress, result.Availability);
        Assert.Null(result.Options);
    }

    [Theory]
    [InlineData(null, "m", "k")]
    [InlineData("https://example.com/v1", null, "k")]
    [InlineData("https://example.com/v1", "m", null)]
    [InlineData("", "m", "k")]
    [InlineData("https://example.com/v1", "  ", "k")]
    [InlineData("https://example.com/v1", "m", "")]
    public void All_three_variables_are_needed(string? url, string? model, string? key)
    {
        var values = new Dictionary<string, string?>
        {
            [AiConfiguration.BaseUrlVariable] = url,
            [AiConfiguration.ModelVariable] = model,
            [AiConfiguration.ApiKeyVariable] = key,
        };

        var result = AiConfiguration.FromEnvironment(name => values[name]);

        Assert.Equal(AiAvailability.MissingVariables, result.Availability);
        Assert.Null(result.Options);
    }

    [Fact]
    public void With_all_three_the_options_are_made_and_hide_the_key_when_printed()
    {
        const string key = "key-placeholder-9f8e7d6c";
        var result = AiConfiguration.FromEnvironment(name => name switch
        {
            AiConfiguration.BaseUrlVariable => " https://api.example.com/v1 ",
            AiConfiguration.ModelVariable => "some-model",
            _ => key,
        });

        Assert.True(result.IsAvailable);
        var text = result.Options!.ToString();
        Assert.DoesNotContain(key, text, StringComparison.Ordinal);
        Assert.DoesNotContain(key, result.ToString(), StringComparison.Ordinal);
        Assert.Contains("some-model", text, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(4), result.Options.Timeout);
        Assert.Equal(32, result.Options.MaxTokens);
    }

    [Fact]
    public void The_variable_names_are_the_documented_ones()
    {
        Assert.Equal("PRESENTA_AI_BASE_URL", AiConfiguration.BaseUrlVariable);
        Assert.Equal("PRESENTA_AI_MODEL", AiConfiguration.ModelVariable);
        Assert.Equal("PRESENTA_AI_API_KEY", AiConfiguration.ApiKeyVariable);
    }
}
