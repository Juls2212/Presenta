using System.ComponentModel;
using System.Windows;
using Presenta.Companion.ViewModels;

namespace Presenta.Companion.App;

public partial class MainWindow : Window
{
    private bool _closeApproved;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// With no running or paused session the window closes as usual. During a session the first close request is held
    /// back, the user is asked, and only a confirmation saves the session and closes (see ADR-64).
    /// </summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeApproved || DataContext is not MainViewModel viewModel || !viewModel.NeedsCloseConfirmation)
        {
            return;
        }

        e.Cancel = true;
        if (_closing)
        {
            return; // a confirmation is already on screen
        }

        _closing = true;
        try
        {
            var approved = await viewModel.RequestCloseAsync(
                (title, message) => MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
            if (approved)
            {
                _closeApproved = true;
                Close();
            }
        }
        catch (Exception)
        {
            // Saving failed in an unexpected way: let the user try again instead of losing the window.
        }
        finally
        {
            _closing = false;
        }
    }
}
