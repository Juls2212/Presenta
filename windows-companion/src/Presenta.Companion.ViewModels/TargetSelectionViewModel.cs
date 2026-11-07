using System.Collections.ObjectModel;
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
        Display = UiText.Format(isSuggested ? "Target_ItemSuggested" : "Target_Item", window.Title, window.ProcessName);
    }

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

    private TargetItem? _selected;
    private bool _applyingSelection;
    private string _statusText = string.Empty;
    private TargetSeverity _severity;
    private bool _keysEnabled = true;
    private int _holdTicks;

    public TargetSelectionViewModel(PresentationTargetService service, IUiDispatcher dispatcher)
    {
        _service = service;
        _ui = dispatcher;
        RefreshCommand = new RelayCommand(RefreshList);
        VerifyCommand = new RelayCommand(() => LastOperation = VerifyAsync());
        RefreshList();
    }

    public ObservableCollection<TargetItem> Items { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand VerifyCommand { get; }

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

    /// <summary>Reads the open windows again, keeps the chosen one selected if it is listed, and updates the status.</summary>
    public void RefreshList()
    {
        var candidates = _service.ListCandidates();
        var resolution = _service.Resolve();
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

        RefreshStatus();
    }

    /// <summary>Recomputes the status line from the current windows.</summary>
    public void RefreshStatus()
    {
        _holdTicks = 0;
        var state = _service.GetState();
        StatusText = UiText.Get(state switch
        {
            TargetState.Ready => "Target_Status_Ready",
            TargetState.Minimized => "Target_Status_Minimized",
            TargetState.NotFound => "Target_Status_NotFound",
            TargetState.Ambiguous => "Target_Status_Ambiguous",
            TargetState.Elevated => "Target_Status_Elevated",
            _ => "Target_Status_None",
        });
        Severity = state switch
        {
            TargetState.Ready or TargetState.Minimized => TargetSeverity.Good,
            TargetState.NoneSelected => TargetSeverity.Neutral,
            _ => TargetSeverity.Warning,
        };
    }

    /// <summary>Brings the chosen window to the front and reports whether it got there. Sends no key.</summary>
    public async Task VerifyAsync()
    {
        var result = await Task.Run(_service.Activate).ConfigureAwait(false);
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
}
