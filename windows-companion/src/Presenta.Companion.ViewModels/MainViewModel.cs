using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Presenta.Server;

namespace Presenta.Companion.ViewModels;

/// <summary>Static captions of the window, read from the resource file. Bound by the XAML.</summary>
public sealed class UiLabels
{
    public string Title => UiText.Get("Window_Title");

    public string Subtitle => UiText.Get("Window_Subtitle");

    public string PairingTitle => UiText.Get("Pairing_Title");

    public string PairingHint => UiText.Get("Pairing_Hint");

    public string PairingRegenerate => UiText.Get("Pairing_Regenerate");

    public string ConnectionTitle => UiText.Get("Connection_Title");

    public string LocalNetworkToggle => UiText.Get("Toggle_LocalNetwork");

    public string LocalNetworkHint => UiText.Get("Toggle_LocalNetwork_Hint");

    public string SendKeysToggle => UiText.Get("Toggle_SendKeys");

    public string SendKeysHint => UiText.Get("Toggle_SendKeys_Hint");

    public string WatchTitle => UiText.Get("Watch_Title");

    public string SessionTitle => UiText.Get("Session_Title");

    public string SessionNone => UiText.Get("Session_None");

    public string SessionPaused => UiText.Get("Session_Paused");

    public string EventsTitle => UiText.Get("Events_Title");

    public string EventsEmpty => UiText.Get("Events_Empty");

    public string TargetTitle => UiText.Get("Target_Title");

    public string TargetHint => UiText.Get("Target_Hint");

    public string TargetRefresh => UiText.Get("Target_Refresh");

    public string TargetVerify => UiText.Get("Target_Verify");

    public string TargetEmpty => UiText.Get("Target_Empty");

    public string TargetKeysOff => UiText.Get("Target_KeysOff");
}

/// <summary>
/// State of the desktop window. Plain classes and <see cref="System.ComponentModel.INotifyPropertyChanged"/>; no WPF types.
/// Service events arrive on background threads and are marshalled through <see cref="IUiDispatcher"/>.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    public const int DefaultMaxEvents = 8;

    private readonly CompanionHost _host;
    private readonly IUiDispatcher _ui;
    private readonly Func<IReadOnlyList<string>> _lanAddresses;
    private readonly TimeZoneInfo _zone;
    private readonly int _maxEvents;

    private bool _isServerRunning;
    private string? _serverError;
    private string _pairingCode;
    private bool _allowLocalNetwork;
    private bool _sendKeys;
    private bool _watchConnected;
    private bool _hasSession;
    private string _sessionName = string.Empty;
    private string _sessionSlideText = string.Empty;
    private string _sessionRemainingText = string.Empty;
    private bool _sessionPaused;
    private bool _sessionOvertime;

    public MainViewModel(
        CompanionHost host,
        IUiDispatcher dispatcher,
        Func<IReadOnlyList<string>>? lanAddresses = null,
        int maxEvents = DefaultMaxEvents,
        TimeZoneInfo? timeZone = null,
        TargetSelectionViewModel? target = null)
    {
        Target = target;
        _host = host;
        _ui = dispatcher;
        _lanAddresses = lanAddresses ?? LocalNetwork.GetLanAddresses;
        _maxEvents = maxEvents;
        _zone = timeZone ?? TimeZoneInfo.Local;
        _pairingCode = host.Service.PairingCode;
        _allowLocalNetwork = host.AllowLocalNetwork;
        _sendKeys = host.Service.SendKeys;
        if (target is not null)
        {
            target.KeysEnabled = _sendKeys;
        }

        ToggleServerCommand = new RelayCommand(() => LastOperation = ToggleServerAsync());
        RegenerateCodeCommand = new RelayCommand(RegenerateCode);

        host.Service.StatusChanged += OnStatusChanged;
        host.Service.ActivityOccurred += OnActivity;
        Apply(host.Service.Status);
        RefreshAddresses();
    }

    public UiLabels Labels { get; } = new();

    /// <summary>The "presentation window" section, or null when the window system is not available.</summary>
    public TargetSelectionViewModel? Target { get; }

    public bool HasTarget => Target is not null;

    public ObservableCollection<string> Addresses { get; } = new();

    /// <summary>Newest first, at most <c>maxEvents</c> lines.</summary>
    public ObservableCollection<string> Events { get; } = new();

    public ICommand ToggleServerCommand { get; }

    public ICommand RegenerateCodeCommand { get; }

    /// <summary>The most recent asynchronous action started from the window. Tests await it.</summary>
    public Task? LastOperation { get; private set; }

    public bool IsServerRunning
    {
        get => _isServerRunning;
        private set
        {
            if (SetProperty(ref _isServerRunning, value))
            {
                Raise(nameof(ServerStatusText));
                Raise(nameof(ToggleServerText));
            }
        }
    }

    public string ServerStatusText => UiText.Get(_isServerRunning ? "Server_Running" : "Server_Stopped");

    public string ToggleServerText => UiText.Get(_isServerRunning ? "Server_Stop" : "Server_Start");

    /// <summary>Why the server could not start (for example the port is in use), or null.</summary>
    public string? ServerError
    {
        get => _serverError;
        private set
        {
            if (SetProperty(ref _serverError, value))
            {
                Raise(nameof(HasServerError));
            }
        }
    }

    public bool HasServerError => _serverError is not null;

    public int Port => _host.Port;

    public string PortText => UiText.Format("Connection_Port", _host.Port.ToString(CultureInfo.InvariantCulture));

    public string PairingCode
    {
        get => _pairingCode;
        private set => SetProperty(ref _pairingCode, value);
    }

    /// <summary>"Allow physical watch on the local network". Changing it rebinds the server.</summary>
    public bool AllowLocalNetwork
    {
        get => _allowLocalNetwork;
        set
        {
            if (SetProperty(ref _allowLocalNetwork, value))
            {
                LastOperation = ApplyLocalNetworkAsync(value);
            }
        }
    }

    /// <summary>"Send keys to the presentation".</summary>
    public bool SendKeys
    {
        get => _sendKeys;
        set
        {
            if (SetProperty(ref _sendKeys, value))
            {
                _host.Service.SetSendKeys(value);
                if (Target is not null)
                {
                    Target.KeysEnabled = value;
                }
            }
        }
    }

    public bool WatchConnected
    {
        get => _watchConnected;
        private set
        {
            if (SetProperty(ref _watchConnected, value))
            {
                Raise(nameof(WatchStatusText));
            }
        }
    }

    public string WatchStatusText => UiText.Get(_watchConnected ? "Watch_Connected" : "Watch_Waiting");

    public bool HasSession
    {
        get => _hasSession;
        private set
        {
            if (SetProperty(ref _hasSession, value))
            {
                Raise(nameof(HasNoSession));
            }
        }
    }

    public bool HasNoSession => !_hasSession;

    public string SessionName
    {
        get => _sessionName;
        private set => SetProperty(ref _sessionName, value);
    }

    public string SessionSlideText
    {
        get => _sessionSlideText;
        private set => SetProperty(ref _sessionSlideText, value);
    }

    public string SessionRemainingText
    {
        get => _sessionRemainingText;
        private set => SetProperty(ref _sessionRemainingText, value);
    }

    public bool SessionPaused
    {
        get => _sessionPaused;
        private set => SetProperty(ref _sessionPaused, value);
    }

    /// <summary>True when the planned time has run out; the window colors the remaining time as a warning.</summary>
    public bool SessionOvertime
    {
        get => _sessionOvertime;
        private set => SetProperty(ref _sessionOvertime, value);
    }

    public bool HasEvents => Events.Count > 0;

    /// <summary>Starts the server. A failure (such as a busy port) is shown in <see cref="ServerError"/>, never thrown.</summary>
    public async Task StartAsync()
    {
        try
        {
            await _host.StartAsync();
            ServerError = null;
        }
        catch (Exception ex)
        {
            ServerError = UiText.Format("Server_Error", ex.Message);
        }

        IsServerRunning = _host.IsRunning;
        Raise(nameof(PortText));
        Raise(nameof(Port));
        RefreshAddresses();
    }

    public async Task StopAsync()
    {
        await _host.StopAsync();
        IsServerRunning = _host.IsRunning;
        RefreshAddresses();
    }

    public void Dispose()
    {
        _host.Service.StatusChanged -= OnStatusChanged;
        _host.Service.ActivityOccurred -= OnActivity;
    }

    private Task ToggleServerAsync() => _isServerRunning ? StopAsync() : StartAsync();

    private void RegenerateCode()
    {
        _host.Service.RegeneratePairingCode();
        PairingCode = _host.Service.PairingCode;
    }

    private async Task ApplyLocalNetworkAsync(bool allow)
    {
        try
        {
            await _host.RebindAsync(allow);
            ServerError = null;
        }
        catch (Exception ex)
        {
            ServerError = UiText.Format("Server_Error", ex.Message);
        }

        IsServerRunning = _host.IsRunning;
        RefreshAddresses();
    }

    private void RefreshAddresses()
    {
        Addresses.Clear();
        var port = _host.Port.ToString(CultureInfo.InvariantCulture);
        Addresses.Add(UiText.Format("Address_Emulator", port));
        Addresses.Add(UiText.Format("Address_Localhost", port));
        if (_host.AllowLocalNetwork)
        {
            foreach (var address in _lanAddresses())
            {
                Addresses.Add(UiText.Format("Address_Lan", address, port));
            }
        }
        else
        {
            Addresses.Add(UiText.Get("Address_LanOff"));
        }
    }

    private void OnStatusChanged(object? sender, CompanionStatus status) => _ui.Post(() => Apply(status));

    private void OnActivity(object? sender, CompanionActivity activity) => _ui.Post(() => AddEvent(activity));

    private void Apply(CompanionStatus status)
    {
        WatchConnected = status.WatchConnected;
        PairingCode = status.PairingCode;
        if (_sendKeys != status.SendKeys)
        {
            _sendKeys = status.SendKeys;
            Raise(nameof(SendKeys));
            if (Target is not null)
            {
                Target.KeysEnabled = _sendKeys;
            }
        }

        var session = status.Session;
        HasSession = session is not null;
        if (session is null)
        {
            SessionName = string.Empty;
            SessionSlideText = string.Empty;
            SessionRemainingText = string.Empty;
            SessionPaused = false;
            SessionOvertime = false;
            return;
        }

        SessionName = session.Name;
        SessionSlideText = UiText.Format("Session_Slide", session.CurrentSlide, session.TotalSlides);
        SessionPaused = session.State == Presenta.Domain.SessionState.Paused;
        SessionOvertime = session.RemainingMs < 0;
        var clock = FormatClock(Math.Abs(session.RemainingMs));
        SessionRemainingText = UiText.Format(SessionOvertime ? "Session_Overtime" : "Session_Remaining", clock);
    }

    private void AddEvent(CompanionActivity activity)
    {
        var text = activity.Kind switch
        {
            ActivityKind.SlideCounterChanged => UiText.Format("Event_SlideCounterChanged", activity.Slide ?? 0),
            ActivityKind.SlideSet => UiText.Format("Event_SlideSet", activity.Slide ?? 0),
            _ => UiText.Get("Event_" + activity.Kind),
        };
        var time = TimeZoneInfo.ConvertTime(activity.At, _zone).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Events.Insert(0, UiText.Format("Event_Line", time, text));
        while (Events.Count > _maxEvents)
        {
            Events.RemoveAt(Events.Count - 1);
        }

        Raise(nameof(HasEvents));
    }

    /// <summary><c>mm:ss</c>, or <c>h:mm:ss</c> from one hour.</summary>
    internal static string FormatClock(long milliseconds)
    {
        var total = milliseconds / 1000;
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{seconds:00}");
    }
}
