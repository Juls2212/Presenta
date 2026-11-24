using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Presenta.Domain;
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

    public string RememberCodeToggle => UiText.Get("Pairing_Remember");

    public string RememberCodeHint => UiText.Get("Pairing_Remember_Hint");

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

    public string TargetDiagnostic => UiText.Get("Target_Diagnostic");

    public string TargetEmpty => UiText.Get("Target_Empty");

    public string TargetKeysOff => UiText.Get("Target_KeysOff");

    public string NavLabel => UiText.Get("Nav_Label");

    public string HomeChecklist => UiText.Get("Home_Checklist");

    public string HomeSession => UiText.Get("Home_Session");

    public string HomeSlideCaption => UiText.Get("Home_SlideCaption");

    public string HomeElapsedCaption => UiText.Get("Home_ElapsedCaption");

    public string HomeRemainingCaption => UiText.Get("Home_RemainingCaption");

    public string HomeOvertimeCaption => UiText.Get("Home_OvertimeCaption");

    public string HomeNoSessionHint => UiText.Get("Home_NoSessionHint");

    public string HomeSeeAll => UiText.Get("Home_SeeAll");

    public string PairCopy => UiText.Get("Pair_Copy");

    public string PairNodeComputer => UiText.Get("Pair_NodeComputer");

    public string PairDiagramName => UiText.Get("Pair_DiagramName");

    public string WindowListName => UiText.Get("Window_ListName");

    public string WindowOthersListName => UiText.Get("Window_OthersListName");

    public string GuideTitle => UiText.Get("Guide_Title");

    public string GuideTag => UiText.Get("Guide_Tag");

    public string GuideMinutes => UiText.Get("Guide_Minutes");

    public string GuideUp => UiText.Get("Guide_Up");

    public string GuideDown => UiText.Get("Guide_Down");

    public string GuideNote => UiText.Get("Guide_Note");

    public string SettingsTech => UiText.Get("Settings_Tech");
}

/// <summary>How the pace pill of Inicio is marked. The words and a mark shape accompany the color.</summary>
public enum PaceTone
{
    Neutral,
    Good,
    Warning,
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
    private readonly IClipboard? _clipboard;

    private bool _isServerRunning;
    private string? _serverError;
    private string _pairingCode;
    private bool _allowLocalNetwork;
    private bool _sendKeys;
    private bool _rememberCode;
    private bool _watchConnected;
    private bool _hasSession;
    private string _sessionName = string.Empty;
    private string _sessionSlideText = string.Empty;
    private string _sessionRemainingText = string.Empty;
    private bool _sessionPaused;
    private bool _sessionOvertime;
    private int _sessionCurrentSlide;
    private int _sessionTotalSlides;
    private string _sessionElapsedText = string.Empty;
    private string _sessionRemainingClock = string.Empty;
    private double _sessionProgressPercent;
    private string _paceText = string.Empty;
    private PaceTone _paceTone;
    private string _copyFeedback = string.Empty;

    public MainViewModel(
        CompanionHost host,
        IUiDispatcher dispatcher,
        Func<IReadOnlyList<string>>? lanAddresses = null,
        int maxEvents = DefaultMaxEvents,
        TimeZoneInfo? timeZone = null,
        TargetSelectionViewModel? target = null,
        AiSectionViewModel? ai = null,
        IClipboard? clipboard = null,
        SessionOptions? alertOptions = null)
    {
        Target = target;
        Ai = ai;
        _clipboard = clipboard;
        _host = host;
        _ui = dispatcher;
        _lanAddresses = lanAddresses ?? LocalNetwork.GetLanAddresses;
        _maxEvents = maxEvents;
        _zone = timeZone ?? TimeZoneInfo.Local;
        _pairingCode = host.Service.PairingCode;
        _allowLocalNetwork = host.AllowLocalNetwork;
        _sendKeys = host.Service.SendKeys;
        _rememberCode = host.Service.RememberPairingCode;
        if (target is not null)
        {
            target.KeysEnabled = _sendKeys;
        }

        ToggleServerCommand = new RelayCommand(() => LastOperation = ToggleServerAsync());
        RegenerateCodeCommand = new RelayCommand(RegenerateCode);
        CopyCodeCommand = new RelayCommand(CopyCode);

        // The pages and the navigation rail. They only read this view model; nothing here starts a session or touches the server.
        TargetList = target is null ? null : new TargetListViewModel(target);
        Guide = new AlertGuideViewModel(alertOptions);
        Navigation = new NavigationViewModel(new PageViewModel[]
        {
            new HomePageViewModel(this),
            new PairPageViewModel(this),
            new WindowPageViewModel(this, TargetList),
            new AiPageViewModel(this, Guide),
            new ActivityPageViewModel(this),
            new SettingsPageViewModel(this),
        });
        Checklist = new[]
        {
            new ChecklistItemViewModel(Section.Pair, UiText.Get("Watch_Title"), Navigation.PairCommand),
            new ChecklistItemViewModel(Section.Window, UiText.Get("Target_Title"), Navigation.WindowCommand),
            new ChecklistItemViewModel(Section.Window, UiText.Get("Check_Keys"), Navigation.WindowCommand),
            new ChecklistItemViewModel(Section.Ai, UiText.Get("Ai_Title"), Navigation.AiCommand),
        };
        if (target is not null)
        {
            target.PropertyChanged += OnSectionChanged;
        }

        if (ai is not null)
        {
            ai.PropertyChanged += OnSectionChanged;
        }

        host.Service.StatusChanged += OnStatusChanged;
        host.Service.ActivityOccurred += OnActivity;
        Apply(host.Service.Status);
        RefreshAddresses();
        RefreshStatuses();
    }

    public UiLabels Labels { get; } = new();

    /// <summary>The "presentation window" section, or null when the window system is not available.</summary>
    public TargetSelectionViewModel? Target { get; }

    public bool HasTarget => Target is not null;

    /// <summary>The "Recomendaciones con IA" section, or null when this window has none.</summary>
    public AiSectionViewModel? Ai { get; }

    public bool HasAi => Ai is not null;

    public ObservableCollection<string> Addresses { get; } = new();

    /// <summary>The navigation rail and the page on screen.</summary>
    public NavigationViewModel Navigation { get; }

    /// <summary>The window list grouped for the Ventana page, or null when the window system is not available.</summary>
    public TargetListViewModel? TargetList { get; }

    /// <summary>The didactic alert timeline of the "IA y alertas" page.</summary>
    public AlertGuideViewModel Guide { get; }

    /// <summary>The four rows of "Antes de empezar": watch, presentation window, keys, AI.</summary>
    public IReadOnlyList<ChecklistItemViewModel> Checklist { get; }

    /// <summary>The six characters of the pairing code, one per box.</summary>
    public IReadOnlyList<string> PairingDigits => _pairingCode.Select(c => c.ToString()).ToArray();

    /// <summary>The code as it is read aloud: the label and the digits separated by spaces.</summary>
    public string PairingCodeSpoken => UiText.Format("Pair_CodeSpoken", string.Join(' ', _pairingCode.Select(c => c.ToString())));

    /// <summary>The same lines as <see cref="Events"/>, with time, text and tone separated. Newest first.</summary>
    public ObservableCollection<ActivityEntryViewModel> ActivityEntries { get; } = new();

    /// <summary>The newest three entries, for Inicio.</summary>
    public ObservableCollection<ActivityEntryViewModel> RecentEntries { get; } = new();

    public bool HasNoEntries => ActivityEntries.Count == 0;

    public bool HasRecentEntries => RecentEntries.Count > 0;

    /// <summary>Newest first, at most <c>maxEvents</c> lines.</summary>
    public ObservableCollection<string> Events { get; } = new();

    public ICommand ToggleServerCommand { get; }

    public ICommand RegenerateCodeCommand { get; }

    /// <summary>Copies the pairing code to the clipboard and says so in <see cref="CopyFeedback"/>.</summary>
    public ICommand CopyCodeCommand { get; }

    /// <summary>"Copiado" (or the failure) after the copy button; empty again when the code changes.</summary>
    public string CopyFeedback
    {
        get => _copyFeedback;
        private set
        {
            if (SetProperty(ref _copyFeedback, value))
            {
                Raise(nameof(HasCopyFeedback));
            }
        }
    }

    public bool HasCopyFeedback => _copyFeedback.Length > 0;

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
                RefreshStatuses();
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
                RefreshStatuses();
            }
        }
    }

    public bool HasServerError => _serverError is not null;

    public int Port => _host.Port;

    public string PortText => UiText.Format("Connection_Port", _host.Port.ToString(CultureInfo.InvariantCulture));

    public string PairingCode
    {
        get => _pairingCode;
        private set
        {
            if (SetProperty(ref _pairingCode, value))
            {
                Raise(nameof(PairingDigits));
                Raise(nameof(PairingCodeSpoken));
                CopyFeedback = string.Empty;
            }
        }
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

    /// <summary>"Remember the pairing code". Off by default; turning it on stores the current code (ADR-69).</summary>
    public bool RememberPairingCode
    {
        get => _rememberCode;
        set
        {
            if (SetProperty(ref _rememberCode, value))
            {
                _host.Service.SetRememberPairingCode(value);
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

                RefreshStatuses();
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
                RefreshStatuses();
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

    /// <summary>Slide number of the active session (0 with none).</summary>
    public int SessionCurrentSlide
    {
        get => _sessionCurrentSlide;
        private set => SetProperty(ref _sessionCurrentSlide, value);
    }

    public int SessionTotalSlides
    {
        get => _sessionTotalSlides;
        private set => SetProperty(ref _sessionTotalSlides, value);
    }

    /// <summary>Time elapsed, as <c>mm:ss</c>.</summary>
    public string SessionElapsedText
    {
        get => _sessionElapsedText;
        private set => SetProperty(ref _sessionElapsedText, value);
    }

    /// <summary>Time left (or past the plan), as <c>mm:ss</c>, without a caption.</summary>
    public string SessionRemainingClock
    {
        get => _sessionRemainingClock;
        private set => SetProperty(ref _sessionRemainingClock, value);
    }

    /// <summary>Share of the planned time that has passed, 0 to 100 (100 in overtime).</summary>
    public double SessionProgressPercent
    {
        get => _sessionProgressPercent;
        private set
        {
            if (SetProperty(ref _sessionProgressPercent, value))
            {
                Raise(nameof(SessionRingName));
            }
        }
    }

    public string SessionRingName => UiText.Format("Home_RingName", (int)Math.Round(_sessionProgressPercent));

    /// <summary>The pace of the presenter, in words. Empty with no session.</summary>
    public string PaceText
    {
        get => _paceText;
        private set => SetProperty(ref _paceText, value);
    }

    public PaceTone PaceTone
    {
        get => _paceTone;
        private set => SetProperty(ref _paceTone, value);
    }

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

    /// <summary>True while a session is running or paused: closing the window then needs the user's confirmation.</summary>
    public bool NeedsCloseConfirmation =>
        _host.Service.Status.Session is { State: Presenta.Domain.SessionState.Running or Presenta.Domain.SessionState.Paused };

    /// <summary>
    /// Handles a request to close the window. With no running or paused session it returns true at once and nothing is
    /// asked. Otherwise <paramref name="confirmCloseAndSave"/> receives the Spanish title and message; when it returns
    /// false the window stays open and nothing changes. When it returns true the session is ended as interrupted by
    /// disconnection (exactly one history record), the server is stopped cleanly, and true is returned (ADR-64).
    /// </summary>
    public async Task<bool> RequestCloseAsync(Func<string, string, bool> confirmCloseAndSave)
    {
        if (NeedsCloseConfirmation)
        {
            if (!confirmCloseAndSave(UiText.Get("Close_Title"), UiText.Get("Close_Message")))
            {
                return false;
            }

            await _host.Service.InterruptActiveSessionAsync();
        }

        await StopAsync();
        return true;
    }

    public void Dispose()
    {
        if (Target is not null)
        {
            Target.PropertyChanged -= OnSectionChanged;
        }

        if (Ai is not null)
        {
            Ai.PropertyChanged -= OnSectionChanged;
        }

        TargetList?.Dispose();
        Ai?.Dispose();
        _host.Service.StatusChanged -= OnStatusChanged;
        _host.Service.ActivityOccurred -= OnActivity;
    }

    private Task ToggleServerAsync() => _isServerRunning ? StopAsync() : StartAsync();

    private void RegenerateCode()
    {
        _host.Service.RegeneratePairingCode();
        PairingCode = _host.Service.PairingCode;
    }

    private void CopyCode()
    {
        try
        {
            if (_clipboard is null)
            {
                throw new InvalidOperationException("No clipboard is available.");
            }

            _clipboard.SetText(_pairingCode);
            CopyFeedback = UiText.Get("Pair_Copied");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            CopyFeedback = UiText.Get("Pair_CopyFailed");
        }
    }

    private void OnSectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshStatuses();

    /// <summary>Recomputes the checklist of Inicio and the status dots of the navigation rail from the state they mirror.</summary>
    private void RefreshStatuses()
    {
        if (Checklist is null || Navigation is null)
        {
            return; // still being built
        }

        var targetState = Target?.Severity;
        var windowOk = targetState == TargetSeverity.Good;
        var windowVerified = windowOk && Target?.IsVerified == true;

        // Watch
        var watchText = WatchStatusText;
        if (_watchConnected)
        {
            Checklist[0].Set(CheckState.Ok, UiText.Get("Check_Watch_Linked"));
            Navigation.SetStatus(Section.Pair, NavStatus.Ok, watchText);
        }
        else if (HasServerError)
        {
            Checklist[0].Set(CheckState.Warning, _serverError ?? watchText);
            Navigation.SetStatus(Section.Pair, NavStatus.Warning, _serverError ?? watchText);
        }
        else
        {
            Checklist[0].Set(CheckState.Empty, watchText);
            Navigation.SetStatus(Section.Pair, NavStatus.Off, watchText);
        }

        // Presentation window
        if (Target is null)
        {
            Checklist[1].Set(CheckState.Empty, UiText.Get("Target_Status_None"));
            Navigation.SetStatus(Section.Window, NavStatus.None, string.Empty);
        }
        else
        {
            // A window that is only saved (or picked automatically) is not "ready" yet: success needs a verification in this run.
            var subtitle = windowVerified && Target.SelectedItem is { } chosen
                ? chosen.Window.Title
                : windowOk ? UiText.Get("Check_Window_Unverified") : Target.StatusText;
            Checklist[1].Set(
                targetState switch
                {
                    TargetSeverity.Good => windowVerified ? CheckState.Ok : CheckState.Warning,
                    TargetSeverity.Warning => CheckState.Warning,
                    _ => CheckState.Empty,
                },
                subtitle);
            Navigation.SetStatus(
                Section.Window,
                targetState switch
                {
                    TargetSeverity.Good => windowVerified ? NavStatus.Ok : NavStatus.Warning,
                    TargetSeverity.Warning => NavStatus.Warning,
                    _ => NavStatus.Off,
                },
                windowOk && !windowVerified ? UiText.Get("Check_Window_Unverified") : Target.StatusText);
        }

        // Keys
        if (!_sendKeys)
        {
            Checklist[2].Set(CheckState.Warning, UiText.Get("Toggle_SendKeys_Hint"));
        }
        else if (windowOk)
        {
            Checklist[2].Set(CheckState.Ok, UiText.Get("Check_Keys_On"));
        }
        else
        {
            Checklist[2].Set(CheckState.Empty, UiText.Get("Check_Keys_Waiting"));
        }

        // AI
        if (Ai is null)
        {
            Checklist[3].Set(CheckState.Empty, UiText.Get("Ai_Status_NotConfigured"));
            Navigation.SetStatus(Section.Ai, NavStatus.None, string.Empty);
        }
        else
        {
            Checklist[3].Set(
                Ai.IsGood ? CheckState.Ok : Ai.IsProblem ? CheckState.Warning : CheckState.Empty,
                Ai.StatusText);
            Navigation.SetStatus(
                Section.Ai,
                Ai.IsGood ? NavStatus.Ok : Ai.IsProblem ? NavStatus.Warning : NavStatus.Off,
                Ai.StatusText);
        }
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
            SessionCurrentSlide = 0;
            SessionTotalSlides = 0;
            SessionElapsedText = string.Empty;
            SessionRemainingClock = string.Empty;
            SessionProgressPercent = 0;
            PaceText = string.Empty;
            PaceTone = PaceTone.Neutral;
            return;
        }

        SessionCurrentSlide = session.CurrentSlide;
        SessionTotalSlides = session.TotalSlides;
        SessionElapsedText = FormatClock(Math.Max(0, session.ElapsedMs));
        SessionRemainingClock = FormatClock(Math.Abs(session.RemainingMs));
        SessionProgressPercent = session.PlannedDurationMs <= 0
            ? 0
            : Math.Clamp(session.ElapsedMs * 100.0 / session.PlannedDurationMs, 0, 100);
        PaceText = UiText.Get("Pace_" + session.PaceStatus);
        PaceTone = session.PaceStatus switch
        {
            Presenta.Domain.PaceStatus.OnTrack => PaceTone.Good,
            Presenta.Domain.PaceStatus.Behind or Presenta.Domain.PaceStatus.TooFast => PaceTone.Warning,
            _ => PaceTone.Neutral,
        };

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
            ActivityKind.KeyFailed when activity.KeyResult is { } result =>
                UiText.Format("Event_KeyFailedReason", UiText.Get("Event_KeyReason_" + result)),
            _ => UiText.Get("Event_" + activity.Kind),
        };
        var time = TimeZoneInfo.ConvertTime(activity.At, _zone).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Events.Insert(0, UiText.Format("Event_Line", time, text));
        while (Events.Count > _maxEvents)
        {
            Events.RemoveAt(Events.Count - 1);
        }

        ActivityEntries.Insert(0, new ActivityEntryViewModel(time, text, ToneOf(activity.Kind)));
        while (ActivityEntries.Count > _maxEvents)
        {
            ActivityEntries.RemoveAt(ActivityEntries.Count - 1);
        }

        RecentEntries.Clear();
        foreach (var entry in ActivityEntries.Take(3))
        {
            RecentEntries.Add(entry);
        }

        Raise(nameof(HasEvents));
        Raise(nameof(HasNoEntries));
        Raise(nameof(HasRecentEntries));
    }

    private static ActivityTone ToneOf(ActivityKind kind) => kind switch
    {
        ActivityKind.WatchConnected or ActivityKind.SessionStarted or ActivityKind.SessionResumed or ActivityKind.SessionFinished
            or ActivityKind.NextKeySent or ActivityKind.PreviousKeySent or ActivityKind.SessionRecovered => ActivityTone.Good,
        ActivityKind.AlertRaised or ActivityKind.SessionPaused or ActivityKind.WatchReplaced => ActivityTone.Warning,
        ActivityKind.WatchDisconnected or ActivityKind.PairingRejected or ActivityKind.PairingLockedOut or ActivityKind.SessionInterrupted
            or ActivityKind.KeyFailed or ActivityKind.StartRejectedNoTarget => ActivityTone.Bad,
        _ => ActivityTone.Neutral,
    };

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
