using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Presenta.Companion.ViewModels;
using Presenta.Keyboard.Windows;
using Presenta.Persistence;
using Presenta.Server;
using Presenta.Targeting;

namespace Presenta.Companion.App;

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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var arguments = LaunchArguments.Parse(e.Args);
            var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Information));
            var history = await SqliteHistoryStore.OpenAsync(arguments.DatabasePath);
            var options = new CompanionServerOptions { Port = arguments.Port ?? 8765 };
            var windows = new Win32WindowSystem();
            var target = new PresentationTargetService(windows, new JsonTargetSettingsStore(arguments.TargetSettingsPath ?? JsonTargetSettingsStore.DefaultPath()));
            var keyboard = new WindowSlideKeyboard(target, windows);
            _host = new CompanionHost(options, keyboard, history, TimeProvider.System, loggerFactory);
            var dispatcher = new WpfDispatcher(Dispatcher);
            _viewModel = new MainViewModel(_host, dispatcher, target: new TargetSelectionViewModel(target, dispatcher));

            var window = new MainWindow { DataContext = _viewModel };
            MainWindow = window;
            window.Show();
            await _viewModel.StartAsync();

            // Windows open and close while the companion runs, so the status line is refreshed every couple of seconds.
            _targetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _targetTimer.Tick += (_, _) => _viewModel.Target?.Tick();
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

        base.OnExit(e);
    }
}
