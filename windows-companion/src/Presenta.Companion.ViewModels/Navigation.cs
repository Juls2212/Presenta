using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Presenta.Companion.ViewModels;

/// <summary>The six pages of the window, in the order of the navigation rail.</summary>
public enum Section
{
    Home,
    Pair,
    Window,
    Ai,
    Activity,
    Settings,
}

/// <summary>The small status dot of a navigation item. Each value also has its own shape and a text, so color is never the only signal.</summary>
public enum NavStatus
{
    /// <summary>No dot (the page has no status).</summary>
    None,

    /// <summary>Ready.</summary>
    Ok,

    /// <summary>Needs attention.</summary>
    Warning,

    /// <summary>Off or waiting.</summary>
    Off,
}

/// <summary>One entry of the navigation rail.</summary>
public sealed class NavItemViewModel : ObservableObject
{
    private bool _isSelected;
    private NavStatus _status;
    private string _statusText = string.Empty;

    internal NavItemViewModel(Section section, ICommand command)
    {
        Section = section;
        Command = command;
    }

    public Section Section { get; }

    /// <summary>Shows this page.</summary>
    public ICommand Command { get; }

    public string Label => UiText.Get("Nav_" + Section);

    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (SetProperty(ref _isSelected, value))
            {
                Raise(nameof(AutomationName));
            }
        }
    }

    public NavStatus Status
    {
        get => _status;
        internal set
        {
            if (SetProperty(ref _status, value))
            {
                Raise(nameof(HasStatus));
                Raise(nameof(AutomationName));
            }
        }
    }

    public bool HasStatus => _status != NavStatus.None;

    /// <summary>What the dot means, in words (tooltip and spoken name).</summary>
    public string StatusText
    {
        get => _statusText;
        internal set
        {
            if (SetProperty(ref _statusText, value))
            {
                Raise(nameof(AutomationName));
            }
        }
    }

    /// <summary>The label, followed by the status when there is one.</summary>
    public string AutomationName => _status == NavStatus.None || _statusText.Length == 0
        ? Label
        : UiText.Format("Nav_ItemWithStatus", Label, _statusText);
}

/// <summary>The base of the page view models. Each page is shown by its own view through a data template.</summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(Section section, MainViewModel main)
    {
        Section = section;
        Main = main;
    }

    public Section Section { get; }

    /// <summary>The window state every page reads from. Pages add no behavior of their own to it.</summary>
    public MainViewModel Main { get; }

    public virtual string Title => UiText.Get("Nav_" + Section);

    public virtual string Lead => string.Empty;
}

public sealed class HomePageViewModel : PageViewModel
{
    public HomePageViewModel(MainViewModel main)
        : base(Section.Home, main)
    {
    }

    public override string Title => UiText.Get("Home_Title");

    public override string Lead => UiText.Get("Home_Lead");
}

public sealed class PairPageViewModel : PageViewModel
{
    public PairPageViewModel(MainViewModel main)
        : base(Section.Pair, main)
    {
    }

    public override string Title => UiText.Get("Pair_Title");

    public override string Lead => UiText.Get("Pair_Lead");
}

public sealed class WindowPageViewModel : PageViewModel
{
    public WindowPageViewModel(MainViewModel main, TargetListViewModel? list)
        : base(Section.Window, main)
    {
        List = list;
    }

    /// <summary>The grouped window list, or null when the window system is not available.</summary>
    public TargetListViewModel? List { get; }

    public bool HasList => List is not null;

    public override string Title => UiText.Get("Target_Title");

    public override string Lead => UiText.Get("Target_Hint");
}

public sealed class AiPageViewModel : PageViewModel
{
    public AiPageViewModel(MainViewModel main, AlertGuideViewModel guide)
        : base(Section.Ai, main)
    {
        Guide = guide;
    }

    /// <summary>The didactic block. It only explains; it never starts a session or touches the server.</summary>
    public AlertGuideViewModel Guide { get; }

    public override string Lead => UiText.Get("Ai_PageLead");
}

public sealed class ActivityPageViewModel : PageViewModel
{
    public ActivityPageViewModel(MainViewModel main)
        : base(Section.Activity, main)
    {
    }

    public override string Lead => UiText.Get("Activity_Lead");
}

public sealed class SettingsPageViewModel : PageViewModel
{
    public SettingsPageViewModel(MainViewModel main)
        : base(Section.Settings, main)
    {
    }

    public override string Lead => UiText.Get("Settings_Lead");
}

/// <summary>Which page is shown. One command per section; the window shows <see cref="CurrentPage"/>.</summary>
public sealed class NavigationViewModel : ObservableObject
{
    private readonly Dictionary<Section, PageViewModel> _pages;
    private Section _selected = Section.Home;

    public NavigationViewModel(IEnumerable<PageViewModel> pages)
    {
        _pages = pages.ToDictionary(p => p.Section);
        var all = Enum.GetValues<Section>().Select(s => new NavItemViewModel(s, new RelayCommand(() => Select(s)))).ToList();
        Items = new ReadOnlyCollection<NavItemViewModel>(all);
        TopItems = new ReadOnlyCollection<NavItemViewModel>(all.Where(i => i.Section != Section.Settings).ToList());
        BottomItems = new ReadOnlyCollection<NavItemViewModel>(all.Where(i => i.Section == Section.Settings).ToList());
        HomeCommand = Items[(int)Section.Home].Command;
        PairCommand = Items[(int)Section.Pair].Command;
        WindowCommand = Items[(int)Section.Window].Command;
        AiCommand = Items[(int)Section.Ai].Command;
        ActivityCommand = Items[(int)Section.Activity].Command;
        SettingsCommand = Items[(int)Section.Settings].Command;
        Items[(int)_selected].IsSelected = true;
    }

    /// <summary>All six items, in the order of <see cref="Section"/>.</summary>
    public IReadOnlyList<NavItemViewModel> Items { get; }

    /// <summary>Inicio to Actividad, the top group of the rail.</summary>
    public IReadOnlyList<NavItemViewModel> TopItems { get; }

    /// <summary>The settings page, alone at the bottom.</summary>
    public IReadOnlyList<NavItemViewModel> BottomItems { get; }

    public ICommand HomeCommand { get; }

    public ICommand PairCommand { get; }

    public ICommand WindowCommand { get; }

    public ICommand AiCommand { get; }

    public ICommand ActivityCommand { get; }

    public ICommand SettingsCommand { get; }

    public Section Selected
    {
        get => _selected;
        private set
        {
            if (value == _selected)
            {
                return;
            }

            Items[(int)_selected].IsSelected = false;
            _selected = value;
            Items[(int)_selected].IsSelected = true;
            Raise();
            Raise(nameof(CurrentPage));
        }
    }

    /// <summary>The view model of the page on screen. The window picks its view by type.</summary>
    public PageViewModel CurrentPage => _pages[_selected];

    public PageViewModel Page(Section section) => _pages[section];

    public void Select(Section section) => Selected = section;

    internal void SetStatus(Section section, NavStatus status, string text)
    {
        var item = Items[(int)section];
        item.Status = status;
        item.StatusText = text;
    }
}
