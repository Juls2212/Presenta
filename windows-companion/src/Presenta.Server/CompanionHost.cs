using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Presenta.Domain.History;
using Presenta.Domain.Keyboard;

namespace Presenta.Server;

/// <summary>
/// Hosts the WebSocket endpoint with Kestrel inside the application process. The session logic lives in
/// <see cref="Service"/>, which outlives the listener: <see cref="RebindAsync"/> changes the listening address (for
/// example when the "allow physical watch" switch changes) without losing the session.
/// Tests build it with port 0, a fake <see cref="TimeProvider"/>, a fake keyboard and a temporary history store.
/// </summary>
public sealed class CompanionHost : IAsyncDisposable
{
    private readonly CompanionServerOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private WebApplication? _app;
    private int _activePort;
    private bool _allowLocalNetwork;

    public CompanionHost(
        CompanionServerOptions options,
        ISlideKeyboard keyboard,
        IHistoryStore history,
        TimeProvider? time = null,
        ILoggerFactory? loggerFactory = null)
    {
        _options = options;
        _allowLocalNetwork = options.AllowLocalNetwork;
        _activePort = options.Port;
        _loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        Service = new CompanionService(options, keyboard, history, time, _loggerFactory);
    }

    public CompanionService Service { get; }

    public bool IsRunning => _app is not null;

    /// <summary>The port actually bound (differs from the option when the option is 0).</summary>
    public int Port => _activePort;

    public bool AllowLocalNetwork => _allowLocalNetwork;

    /// <summary>The endpoints Kestrel is bound to: 127.0.0.1 only by default, 0.0.0.0 with the local network switch.</summary>
    public IReadOnlyList<IPEndPoint> BoundEndpoints { get; private set; } = Array.Empty<IPEndPoint>();

    /// <summary>Starts listening. Throws if the port is already in use.</summary>
    public async Task StartAsync()
    {
        if (_app is not null)
        {
            return;
        }

        var address = _allowLocalNetwork ? IPAddress.Any : IPAddress.Loopback;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_loggerFactory);
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Listen(address, _activePort);
        });

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
        app.Run(HandleRequestAsync);
        await app.StartAsync().ConfigureAwait(false);

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        BoundEndpoints = addresses
            .Select(a => new Uri(a))
            .Select(u => new IPEndPoint(IPAddress.Parse(u.Host), u.Port))
            .ToArray();
        _activePort = BoundEndpoints[0].Port;
        _app = app;
    }

    /// <summary>Stops listening and closes any watch connection. The session and history are kept.</summary>
    public async Task StopAsync()
    {
        var app = _app;
        if (app is null)
        {
            return;
        }

        _app = null;
        BoundEndpoints = Array.Empty<IPEndPoint>();
        await Service.CloseAllConnectionsAsync().ConfigureAwait(false);
        await app.StopAsync().ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Switches between loopback only and all interfaces, keeping the same port.</summary>
    public async Task RebindAsync(bool allowLocalNetwork)
    {
        var wasRunning = IsRunning;
        await StopAsync().ConfigureAwait(false);
        _allowLocalNetwork = allowLocalNetwork;
        if (wasRunning)
        {
            await StartAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await Service.DisposeAsync().ConfigureAwait(false);
    }

    private async Task HandleRequestAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await context.Response.WriteAsync("This endpoint speaks WebSocket (Presenta protocol v1).").ConfigureAwait(false);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        await Service.HandleConnectionAsync(socket, context.Connection.RemoteIpAddress, context.RequestAborted).ConfigureAwait(false);
    }
}
