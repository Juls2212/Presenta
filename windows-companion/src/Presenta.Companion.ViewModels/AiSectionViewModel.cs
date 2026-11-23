using Presenta.Server;

namespace Presenta.Companion.ViewModels;

/// <summary>
/// The "Recomendaciones con IA" section: the switch (off by default, disabled with an explanation when the environment
/// variables are missing), the status line, the privacy note and the last text produced (ADR-79). It only talks to the
/// enricher; it never sees the key, which is not in the enricher either. No WPF types; text comes from
/// <c>UiStrings.resx</c>.
/// </summary>
public sealed class AiSectionViewModel : ObservableObject, IDisposable
{
    private readonly AiRecommendationEnricher _ai;
    private readonly IUiDispatcher _ui;

    private bool _enabled;
    private string _statusText = string.Empty;
    private string _lastTextLine = string.Empty;
    private AiStatus _status;

    public AiSectionViewModel(AiRecommendationEnricher ai, IUiDispatcher dispatcher)
    {
        _ai = ai;
        _ui = dispatcher;
        _ai.Changed += OnChanged;
        Refresh();
    }

    public string Title => UiText.Get("Ai_Title");

    public string ToggleLabel => UiText.Get("Ai_Toggle");

    public string PrivacyNote => UiText.Get("Ai_Privacy");

    /// <summary>False when the three environment variables are not usable: the switch is then disabled.</summary>
    public bool CanToggle => _ai.IsAvailable;

    /// <summary>The user's switch. Setting it on an unavailable AI does nothing.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _ai.Enabled = value;
            Refresh();
        }
    }

    public AiStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                Raise(nameof(IsProblem));
                Raise(nameof(IsGood));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>The newest text produced, or a line saying there is none yet.</summary>
    public string LastTextLine
    {
        get => _lastTextLine;
        private set => SetProperty(ref _lastTextLine, value);
    }

    /// <summary>Paused, refused key, or not available: the status is colored as a warning.</summary>
    public bool IsProblem => _status is AiStatus.Paused or AiStatus.InvalidKey or AiStatus.InvalidAddress or AiStatus.InsecureAddress;

    public bool IsGood => _status is AiStatus.Ready or AiStatus.Working;

    /// <summary>Called by a timer every couple of seconds: a pause ends with time, not with an event.</summary>
    public void Tick() => Refresh();

    public void Dispose() => _ai.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e) => _ui.Post(Refresh);

    private void Refresh()
    {
        var status = _ai.Status;
        var enabled = _ai.Enabled;
        if (_enabled != enabled)
        {
            _enabled = enabled;
            Raise(nameof(Enabled));
        }

        Raise(nameof(CanToggle));
        Status = status;
        StatusText = UiText.Get("Ai_Status_" + status);
        LastTextLine = _ai.LastText is { } text ? UiText.Format("Ai_LastText", text) : UiText.Get("Ai_LastTextNone");
    }
}
