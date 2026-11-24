using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Presenta.Domain;

namespace Presenta.Companion.ViewModels;

/// <summary>State of a row of the "Antes de empezar" list. Each state has its own mark shape and a spoken word.</summary>
public enum CheckState
{
    /// <summary>Nothing to do here.</summary>
    Ok,

    /// <summary>Needs attention.</summary>
    Warning,

    /// <summary>Not set up yet, or waiting.</summary>
    Empty,
}

/// <summary>One row of the checklist of Inicio. Activating it shows the related page.</summary>
public sealed class ChecklistItemViewModel : ObservableObject
{
    private CheckState _state = CheckState.Empty;
    private string _subtitle = string.Empty;

    internal ChecklistItemViewModel(Section target, string title, ICommand command)
    {
        Target = target;
        Title = title;
        Command = command;
    }

    /// <summary>The page this row leads to.</summary>
    public Section Target { get; }

    public string Title { get; }

    public ICommand Command { get; }

    public CheckState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                Raise(nameof(StateText));
                Raise(nameof(AutomationName));
            }
        }
    }

    public string Subtitle
    {
        get => _subtitle;
        private set
        {
            if (SetProperty(ref _subtitle, value))
            {
                Raise(nameof(AutomationName));
            }
        }
    }

    public string StateText => UiText.Get("Check_State_" + _state);

    public string AutomationName => UiText.Format("Check_Item", Title, StateText, _subtitle);

    internal void Set(CheckState state, string subtitle)
    {
        State = state;
        Subtitle = subtitle;
    }
}

/// <summary>How an activity line is marked. Each tone has its own mark shape.</summary>
public enum ActivityTone
{
    Neutral,
    Good,
    Warning,
    Bad,
}

/// <summary>One line of the activity list: time, text and a tone.</summary>
public sealed record ActivityEntryViewModel(string Time, string Text, ActivityTone Tone)
{
    /// <summary>The same text as the plain line of <see cref="MainViewModel.Events"/>.</summary>
    public string Line => UiText.Format("Event_Line", Time, Text);
}

/// <summary>One window of the list, with the choice mirrored from the selection of <see cref="TargetSelectionViewModel"/>.</summary>
public sealed class TargetRowViewModel : ObservableObject
{
    private readonly TargetSelectionViewModel _target;

    internal TargetRowViewModel(TargetSelectionViewModel target, TargetItem item)
    {
        _target = target;
        Item = item;
    }

    public TargetItem Item { get; }

    public string Display => Item.Display;

    /// <summary>
    /// True for the chosen window. Setting it to true chooses the window exactly as selecting it in the old list did; setting it to
    /// false does nothing (a choice is replaced by another choice).
    /// </summary>
    public bool IsChosen
    {
        get => ReferenceEquals(_target.SelectedItem, Item);
        set
        {
            if (value)
            {
                _target.SelectedItem = Item;
            }
            else
            {
                Raise(nameof(IsChosen)); // a deselection by the control is undone in the view
            }
        }
    }

    internal void Refresh() => Raise(nameof(IsChosen));
}

/// <summary>
/// The window list of the Ventana page in three groups: the suggested slide shows first, the other PowerPoint windows next, and
/// every remaining window in a collapsed group. It reads <see cref="TargetSelectionViewModel.Items"/> and only grouping happens
/// here: every window of the source is in exactly one group, and choosing one goes through the same selection as before.
/// </summary>
public sealed class TargetListViewModel : ObservableObject, IDisposable
{
    private bool _othersExpanded;

    public TargetListViewModel(TargetSelectionViewModel target)
    {
        Target = target;
        target.Items.CollectionChanged += OnItemsChanged;
        target.PropertyChanged += OnTargetChanged;
        Rebuild();
    }

    public TargetSelectionViewModel Target { get; }

    /// <summary>Slide-show windows of PowerPoint: the entries that start with "Sugerida".</summary>
    public ObservableCollection<TargetRowViewModel> Suggested { get; } = new();

    /// <summary>Other windows of PowerPoint (the editing window, for example).</summary>
    public ObservableCollection<TargetRowViewModel> Presentation { get; } = new();

    /// <summary>All the remaining windows, behind the expander.</summary>
    public ObservableCollection<TargetRowViewModel> Others { get; } = new();

    public bool HasSuggested => Suggested.Count > 0;

    public bool HasPresentation => Presentation.Count > 0;

    public bool HasOthers => Others.Count > 0;

    public string OthersLabel => UiText.Format("Window_Others", Others.Count);

    /// <summary>Whether the expander is open. It opens by itself when the chosen window is inside it, so a choice is never hidden.</summary>
    public bool OthersExpanded
    {
        get => _othersExpanded;
        set => SetProperty(ref _othersExpanded, value);
    }

    public void Dispose()
    {
        Target.Items.CollectionChanged -= OnItemsChanged;
        Target.PropertyChanged -= OnTargetChanged;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnTargetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TargetSelectionViewModel.SelectedItem))
        {
            RefreshChoice();
        }
    }

    private void Rebuild()
    {
        Suggested.Clear();
        Presentation.Clear();
        Others.Clear();
        foreach (var item in Target.Items)
        {
            var row = new TargetRowViewModel(Target, item);
            if (item.IsSuggested)
            {
                Suggested.Add(row);
            }
            else if (item.IsOtherPowerPointWindow)
            {
                Presentation.Add(row);
            }
            else
            {
                Others.Add(row);
            }
        }

        Raise(nameof(HasSuggested));
        Raise(nameof(HasPresentation));
        Raise(nameof(HasOthers));
        Raise(nameof(OthersLabel));
        RefreshChoice();
    }

    private void RefreshChoice()
    {
        foreach (var row in Suggested.Concat(Presentation).Concat(Others))
        {
            row.Refresh();
        }

        if (Others.Any(r => r.IsChosen))
        {
            OthersExpanded = true;
        }
    }
}

/// <summary>The time of one alert in the didactic timeline, and where it sits on the bar.</summary>
public sealed record AlertMarker(AlertKind Kind, string Label, string TimeText, double Fraction, int Row)
{
    // Each marker has its own row (0, 1, 2), so labels near the end of the bar never overlap.
    /// <summary>The position used to draw the label: kept away from the ends of the bar so the text always fits.</summary>
    public double Position => Math.Clamp(Fraction, 0.08, 0.92);

    public string AutomationName => UiText.Format("Guide_Marker", Label, TimeText);
}

/// <summary>
/// The didactic alert timeline block: for a planned duration, when each time alert would fire. The times are not computed here:
/// they come from <see cref="SessionOptions"/>, the same rules the session engine uses. It is explanatory only.
/// </summary>
public sealed class AlertGuideViewModel : ObservableObject
{
    public const int MinMinutes = 1;
    public const int MaxMinutes = 60;
    public const int DefaultMinutes = 15;

    private readonly SessionOptions _options;
    private int _minutes = DefaultMinutes;

    public AlertGuideViewModel(SessionOptions? options = null)
    {
        _options = options ?? new SessionOptions();
        StepUpCommand = new RelayCommand(() => Minutes++);
        StepDownCommand = new RelayCommand(() => Minutes--);
        Recalculate();
    }

    public ICommand StepUpCommand { get; }

    public ICommand StepDownCommand { get; }

    public int Minutes
    {
        get => _minutes;
        set
        {
            var clamped = Math.Clamp(value, MinMinutes, MaxMinutes);
            if (SetProperty(ref _minutes, clamped))
            {
                Recalculate();
                Raise(nameof(CanStepUp));
                Raise(nameof(CanStepDown));
                Raise(nameof(MinutesName));
                Raise(nameof(BarName));
            }
        }
    }

    public bool CanStepUp => _minutes < MaxMinutes;

    public bool CanStepDown => _minutes > MinMinutes;

    public string MinutesName => UiText.Format("Guide_Value", _minutes);

    public string BarName => UiText.Format("Guide_BarName", _minutes);

    public TimeSpan Planned => TimeSpan.FromMinutes(_minutes);

    /// <summary>Elapsed time at which "time running out" fires.</summary>
    public TimeSpan RunningOutAt { get; private set; }

    /// <summary>Elapsed time at which "time critical" fires.</summary>
    public TimeSpan CriticalAt { get; private set; }

    /// <summary>Elapsed time at which the planned time is exceeded.</summary>
    public TimeSpan OvertimeAt => Planned;

    /// <summary>
    /// False when the two thresholds coincide (very short presentations): the engine then only ever raises the critical alert,
    /// so the timeline has no separate running-out marker.
    /// </summary>
    public bool HasRunningOutMarker { get; private set; }

    /// <summary>Share of the bar before any alert, between the running-out and critical alerts, and after the critical one.</summary>
    public double CalmFraction { get; private set; }

    public double RunningOutFraction { get; private set; }

    public double CriticalFraction { get; private set; }

    public IReadOnlyList<AlertMarker> Markers { get; private set; } = Array.Empty<AlertMarker>();

    private void Recalculate()
    {
        var planned = Planned;
        var runningOutThreshold = _options.GetTimeRunningOutThreshold(planned);
        var criticalThreshold = _options.GetTimeCriticalThreshold(planned);
        RunningOutAt = planned - runningOutThreshold;
        CriticalAt = planned - criticalThreshold;
        HasRunningOutMarker = runningOutThreshold > criticalThreshold;

        var total = planned.TotalSeconds;
        var runningOutFraction = RunningOutAt.TotalSeconds / total;
        var criticalFraction = CriticalAt.TotalSeconds / total;
        CalmFraction = HasRunningOutMarker ? runningOutFraction : criticalFraction;
        RunningOutFraction = HasRunningOutMarker ? criticalFraction - runningOutFraction : 0;
        CriticalFraction = 1 - criticalFraction;

        var markers = new List<AlertMarker>();
        if (HasRunningOutMarker)
        {
            markers.Add(new AlertMarker(AlertKind.TimeRunningOut, UiText.Get("Guide_RunningOut"), Clock(RunningOutAt), runningOutFraction, markers.Count));
        }

        markers.Add(new AlertMarker(AlertKind.TimeCritical, UiText.Get("Guide_Critical"), Clock(CriticalAt), criticalFraction, markers.Count));
        markers.Add(new AlertMarker(AlertKind.Overtime, UiText.Get("Guide_Overtime"), Clock(planned), 1, markers.Count));
        Markers = markers;

        Raise(nameof(RunningOutAt));
        Raise(nameof(CriticalAt));
        Raise(nameof(OvertimeAt));
        Raise(nameof(Planned));
        Raise(nameof(HasRunningOutMarker));
        Raise(nameof(CalmFraction));
        Raise(nameof(RunningOutFraction));
        Raise(nameof(CriticalFraction));
        Raise(nameof(Markers));
    }

    private static string Clock(TimeSpan value) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalMinutes}:{value.Seconds:00}");
}
