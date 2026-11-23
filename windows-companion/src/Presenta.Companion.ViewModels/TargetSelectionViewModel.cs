using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Presenta.Domain.Keyboard;
using Presenta.Targeting;

namespace Presenta.Companion.ViewModels;

/// <summary>One row of the window list.</summary>
public sealed class TargetItem
{
    public TargetItem(WindowInfo window, bool isSuggested)
    {
        Window = window;
        IsSuggested = isSuggested;
        IsOtherPowerPointWindow = !isSuggested
            && string.Equals(window.ProcessName, PresentationTargetService.SuggestedProcessName, StringComparison.OrdinalIgnoreCase);
        Display = UiText.Format(
            isSuggested ? "Target_ItemSuggested" : IsOtherPowerPointWindow ? "Target_ItemPowerPointOther" : "Target_Item",
            window.Title,
            window.ProcessName);
    }

    /// <summary>A PowerPoint slide-show window (the suggested kind): its text starts with "Sugerida".</summary>
    public bool IsSlideShow => IsSuggested;

    /// <summary>A window of PowerPoint that is not the slide show (the editing window, for example): its text says so.</summary>
    public bool IsOtherPowerPointWindow { get; }

    public WindowInfo Window { get; }

    public bool IsSuggested { get; }

    /// <summary>Title and program name, ready to show.</summary>
    public string Display { get; }
}

/// <summary>How the status line should be colored.</summary>
public enum TargetSeverity
{
    Neutral,
    Good,
    Warning,
}

/// <summary>
/// The "presentation window" section of the companion window: pick the window the keys go to, refresh the list, verify
/// the choice, and see its status. Verifying brings the window to the front but never presses a key.
/// No WPF types; text comes from <c>UiStrings.resx</c>.
/// </summary>
public sealed class TargetSelectionViewModel : ObservableObject
{
    private readonly PresentationTargetService _service;
    private readonly IUiDispatcher _ui;
    private readonly IClipboard? _clipboard;

    private TargetItem? _selected;
    private bool _applyingSelection;
    private string _statusText = string.Empty;
    private TargetSeverity _severity;
    private bool _keysEnabled = true;
    private int _holdTicks;
    private bool _autoSelected;
    private long _autoSelectedHandle;
    private bool _chooseOne;
    private string _listKey = string.Empty;
    private string? _notSlideShowWarning;

    public TargetSelectionViewModel(PresentationTargetService service, IUiDispatcher dispatcher, IClipboard? clipboard = null)
    {
        _service = service;
        _ui = dispatcher;
        _clipboard = clipboard;
        RefreshCommand = new RelayCommand(RefreshList);
        VerifyCommand = new RelayCommand(() => LastOperation = VerifyAsync());
        CopyDiagnosticCommand = new RelayCommand(() => LastOperation = CopyDiagnosticAsync());
        RefreshList();
    }

    public ObservableCollection<TargetItem> Items { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand VerifyCommand { get; }

    /// <summary>Runs the activation without any key and copies the plain-text diagnostic report to the clipboard.</summary>
    public ICommand CopyDiagnosticCommand { get; }

    /// <summary>The most recent asynchronous action (verify). Tests await it.</summary>
    public Task? LastOperation { get; private set; }

    public bool HasItems => Items.Count > 0;

    public bool HasNoItems => Items.Count == 0;

    /// <summary>The chosen row. Setting it saves the choice; it is null when nothing is chosen or the chosen window is not listed.</summary>
    public TargetItem? SelectedItem
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value) || _applyingSelection)
            {
                return;
            }

            // The user chose by hand: the "chosen automatically" note no longer applies.
            _autoSelected = false;
            _chooseOne = false;

            if (value is not null)
            {
                _service.Select(value.Window);
            }

            RefreshStatus();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public TargetSeverity Severity
    {
        get => _severity;
        private set
        {
            if (SetProperty(ref _severity, value))
            {
                Raise(nameof(IsGood));
                Raise(nameof(IsWarning));
            }
        }
    }

    /// <summary>
    /// Shown next to the status when the chosen window belongs to PowerPoint but is not the slide show (the editing window,
    /// for example). Null for any other window, including browsers and Notepad.
    /// </summary>
    public string? NotSlideShowWarning
    {
        get => _notSlideShowWarning;
        private set
        {
            if (SetProperty(ref _notSlideShowWarning, value))
            {
                Raise(nameof(HasNotSlideShowWarning));
            }
        }
    }

    public bool HasNotSlideShowWarning => _notSlideShowWarning is not null;

    public bool IsGood => _severity == TargetSeverity.Good;

    public bool IsWarning => _severity == TargetSeverity.Warning;

    /// <summary>Whether "send keys to the presentation" is on. When it is off a note says no window is needed.</summary>
    public bool KeysEnabled
    {
        get => _keysEnabled;
        set
        {
            if (SetProperty(ref _keysEnabled, value))
            {
                Raise(nameof(KeysOffNoteVisible));
            }
        }
    }

    public bool KeysOffNoteVisible => !_keysEnabled;

    /// <summary>
    /// Reads the open windows again, keeps the chosen one selected if it is listed, and updates the status. When nothing is
    /// chosen (or the chosen window is gone) and exactly one PowerPoint slide-show window is open, that window is chosen
    /// and the status line says so. With several slide shows nothing is chosen and the status asks the user to pick one.
    /// It never verifies and never presses a key.
    /// </summary>
    public void RefreshList()
    {
        var candidates = _service.ListCandidates();
        var resolution = _service.Resolve();
        var suggested = candidates.Where(c => c.IsSuggested).ToList();
        var unresolved = resolution.State is TargetState.NoneSelected or TargetState.NotFound;
        var autoChosen = false;
        if (unresolved && suggested.Count == 1 && _service.Select(suggested[0].Window))
        {
            resolution = _service.Resolve();
            autoChosen = true;
            _autoSelectedHandle = suggested[0].Window.Handle;
        }

        _chooseOne = unresolved && !autoChosen && suggested.Count > 1;
        _autoSelected = autoChosen || (_autoSelected && resolution.Window?.Handle == _autoSelectedHandle);
        _listKey = ListKey(candidates);
        _applyingSelection = true;
        try
        {
            Items.Clear();
            TargetItem? match = null;
            foreach (var candidate in candidates)
            {
                var item = new TargetItem(candidate.Window, candidate.IsSuggested);
                Items.Add(item);
                if (resolution.Window is { } chosen && chosen.Handle == candidate.Window.Handle)
                {
                    match = item;
                }
            }

            SelectedItem = match;
        }
        finally
        {
            _applyingSelection = false;
        }

        Raise(nameof(HasItems));
        Raise(nameof(HasNoItems));
        RefreshStatus();
    }

    private static string ListKey(IReadOnlyList<TargetCandidate> candidates) =>
        string.Join('|', candidates.Select(c => c.Window.Handle.ToString(CultureInfo.InvariantCulture) + ":" + c.Window.Title));

    /// <summary>
    /// Called by a timer every couple of seconds. It refreshes the status, except for a few ticks after a verification so
    /// the verification result stays readable.
    /// </summary>
    public void Tick()
    {
        if (_holdTicks > 0)
        {
            _holdTicks--;
            return;
        }

        // While nothing usable is chosen, a presentation that starts later is picked up without pressing "Actualizar lista".
        if (_service.GetState() is TargetState.NoneSelected or TargetState.NotFound
            && ListKey(_service.ListCandidates()) != _listKey)
        {
            RefreshList();
            return;
        }

        RefreshStatus();
    }

    /// <summary>Recomputes the status line from the current windows.</summary>
    public void RefreshStatus()
    {
        _holdTicks = 0;
        var resolution = _service.Resolve();
        var state = resolution.State;
        StatusText = UiText.Get(state switch
        {
            TargetState.Ready when _autoSelected => "Target_Status_AutoSelected",
            TargetState.Ready => "Target_Status_Ready",
            TargetState.Minimized => "Target_Status_Minimized",
            TargetState.NotFound or TargetState.NoneSelected when _chooseOne => "Target_Status_ChooseOne",
            TargetState.NotFound => "Target_Status_NotFound",
            TargetState.Ambiguous => "Target_Status_Ambiguous",
            TargetState.Elevated => "Target_Status_Elevated",
            _ => "Target_Status_None",
        });
        NotSlideShowWarning = resolution.Window is { } window && IsPowerPointButNotSlideShow(window)
            ? UiText.Get("Target_Warning_NotSlideShow")
            : null;
        Severity = state switch
        {
            TargetState.Ready or TargetState.Minimized => TargetSeverity.Good,
            TargetState.NoneSelected => TargetSeverity.Neutral,
            _ => TargetSeverity.Warning,
        };
    }

    private static bool IsPowerPointButNotSlideShow(WindowInfo window) =>
        string.Equals(window.ProcessName, PresentationTargetService.SuggestedProcessName, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(window.ClassName, PresentationTargetService.SuggestedWindowClass, StringComparison.Ordinal);

    /// <summary>Brings the chosen window to the front and reports whether it got there. Sends no key.</summary>
    public async Task VerifyAsync()
    {
        var result = await Task.Run(() => _service.Activate()).ConfigureAwait(false);
        _ui.Post(() =>
        {
            _holdTicks = 3;
            if (result.IsInForeground)
            {
                StatusText = UiText.Get("Target_Verify_Ok");
                Severity = TargetSeverity.Good;
                return;
            }

            StatusText = UiText.Get(result.Outcome switch
            {
                ActivationOutcome.NoTargetSelected => "Target_Status_None",
                ActivationOutcome.TargetNotFound => "Target_Status_NotFound",
                ActivationOutcome.TargetAmbiguous => "Target_Status_Ambiguous",
                ActivationOutcome.TargetElevated => "Target_Status_Elevated",
                ActivationOutcome.NotConfirmedForeground => "Target_Verify_NotConfirmed",
                _ => "Target_Verify_Refused",
            });
            Severity = result.Outcome == ActivationOutcome.NoTargetSelected ? TargetSeverity.Neutral : TargetSeverity.Warning;
        });
    }

    /// <summary>
    /// Makes the diagnostic report (the same activation as a real send, no key) and puts it on the clipboard. The status line
    /// then says it was copied, or that copying failed.
    /// </summary>
    public async Task CopyDiagnosticAsync()
    {
        var report = await Task.Run(() => _service.CreateDiagnosticReport()).ConfigureAwait(false);
        _ui.Post(() =>
        {
            _holdTicks = 3;
            try
            {
                if (_clipboard is null)
                {
                    throw new InvalidOperationException("No clipboard is available.");
                }

                _clipboard.SetText(report);
                StatusText = UiText.Get("Target_Diagnostic_Copied");
                Severity = TargetSeverity.Good;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
            {
                StatusText = UiText.Get("Target_Diagnostic_Failed");
                Severity = TargetSeverity.Warning;
            }
        });
    }
}
