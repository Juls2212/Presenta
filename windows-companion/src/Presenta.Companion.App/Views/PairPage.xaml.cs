using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Presenta.Companion.ViewModels;

namespace Presenta.Companion.App.Views;

/// <summary>
/// The links of the diagram move only while the watch is connected, and never when Windows is set to reduce animations
/// (<see cref="SystemParameters.ClientAreaAnimation"/>). The animation stops when the page leaves the screen.
/// </summary>
public partial class PairPage : UserControl
{
    private MainViewModel? _main;

    public PairPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        Unloaded += (_, _) => Animate(false);
        Loaded += (_, _) => Animate(_main?.WatchConnected == true);
    }

    private void Attach()
    {
        if (_main is not null)
        {
            _main.PropertyChanged -= OnMainChanged;
        }

        _main = (DataContext as PairPageViewModel)?.Main;
        if (_main is not null)
        {
            _main.PropertyChanged += OnMainChanged;
        }
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.WatchConnected))
        {
            Animate(_main?.WatchConnected == true && IsLoaded);
        }
    }

    private void Animate(bool on)
    {
        foreach (var line in new Line[] { LinkOne, LinkTwo })
        {
            if (on && SystemParameters.ClientAreaAnimation)
            {
                line.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(0, -6, TimeSpan.FromSeconds(0.6)) { RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                line.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            }
        }
    }
}
