using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Presenta.Ai;
using Presenta.Companion.ViewModels;
using Presenta.Keyboard.Windows;
using Presenta.Persistence;
using Presenta.Server;
using Presenta.Targeting;

namespace Presenta.Companion.App;

/// <summary>Puts text on the Windows clipboard. Called on the UI thread.</summary>
internal sealed class WpfClipboard : IClipboard
{
    public void SetText(string text) => Clipboard.SetText(text);
}

/// <summary>Runs actions on the WPF UI thread.</summary>
internal sealed class WpfDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Post(Action action) => _dispatcher.BeginInvoke(action);
}

public partial class App : Application
{
    private CompanionHost? _host;
    private MainViewModel? _viewModel;
    private DispatcherTimer? _targetTimer;
    private IInstanceLock? _instanceLock;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one companion per Windows session. A second one explains why and exits before touching anything.
        _instanceLock = new NamedEventInstanceLock();
        if (!SingleInstanceGate.TryEnter(
                _instanceLock,
                message => MessageBox.Show(message, UiText.Get("Window_Title"), MessageBoxButton.OK, MessageBoxImage.Information)))
        {
            Shutdown(0);
            return;
        }

        try
        {
            var arguments = LaunchArguments.Parse(e.Args);
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddDebug().SetMinimumLevel(LogLevel.Information);
#if DEBUG
                // Debug builds only: key delivery timings (RNF 03) in %LOCALAPPDATA%\Presenta\timing-debug.log.
                builder.AddProvider(new TimingFileLoggerProvider(
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Presenta", "timing-debug.log")));
#endif
            });
            var history = await SqliteHistoryStore.OpenAsync(arguments.DatabasePath);
            var options = new CompanionServerOptions { Port = arguments.Port ?? 8765 };
            var windows = new Win32WindowSystem();
            var target = new PresentationTargetService(windows, new JsonTargetSettingsStore(arguments.TargetSettingsPath ?? JsonTargetSettingsStore.DefaultPath()));
#if DEBUG
            // Debug builds only: one line per activation attempt, with the Win32 error codes, in the debug output.
            target.Diagnostics = line => loggerFactory.CreateLogger("Targeting").LogInformation("PresentaDebug {Line}", line);
#endif
            var keyboard = new WindowSlideKeyboard(target, windows);
            var activeSessions = new JsonActiveSessionStore(arguments.ActiveSessionPath ?? JsonActiveSessionStore.DefaultPath());
            var settings = new JsonCompanionSettingsStore(arguments.SettingsPath ?? JsonCompanionSettingsStore.DefaultPath());
            // Optional AI wording (ADR-73): the settings come from the environment and are never stored; the switch starts off.
            var aiConfiguration = AiConfiguration.FromEnvironment(Environment.GetEnvironmentVariable);
            var aiHandler = new System.Net.Http.SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
            IAiTextClient? aiClient = aiConfiguration.Options is null
                ? null
                : new OpenAiCompatibleClient(new System.Net.Http.HttpClient(aiHandler) { Timeout = Timeout.InfiniteTimeSpan }, aiConfiguration.Options, TimeProvider.System);
            var ai = new AiRecommendationEnricher(aiClient, aiConfiguration.Availability, new AiEnrichmentOptions(), TimeProvider.System, loggerFactory);
            _host = new CompanionHost(options, keyboard, history, TimeProvider.System, loggerFactory, activeSessions, settings, ai);
            var dispatcher = new WpfDispatcher(Dispatcher);
            _viewModel = new MainViewModel(_host, dispatcher, target: new TargetSelectionViewModel(target, dispatcher, new WpfClipboard()), ai: new AiSectionViewModel(ai, dispatcher), clipboard: new WpfClipboard());

            // High contrast: the design tokens give way to the system colors, now and whenever the setting changes.
            HighContrastTokens.Apply(Resources);
            SystemParameters.StaticPropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SystemParameters.HighContrast))
                {
                    Dispatcher.BeginInvoke(() => HighContrastTokens.Apply(Resources));
                }
            };

            var window = new MainWindow { DataContext = _viewModel };
            MainWindow = window;
            window.Show();
            await _viewModel.StartAsync();

            // Windows open and close while the companion runs, so the status line is refreshed every couple of seconds.
            _targetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _targetTimer.Tick += (_, _) =>
            {
                _viewModel.Target?.Tick();
                _viewModel.Ai?.Tick();
            };
            _targetTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.Format("Startup_Error", ex.Message), UiText.Get("Window_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _targetTimer?.Stop();
        _viewModel?.Dispose();
        if (_host is not null)
        {
            // Closing the window stops the server cleanly: the watch gets a close frame and the port is released.
            Task.Run(() => _host.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(8));
        }

        _instanceLock?.Dispose();
        base.OnExit(e);
    }
}
