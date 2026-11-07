using System.ComponentModel;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Presenta.Companion.ViewModels;

/// <summary>Looks up the Spanish UI text by English key. The text lives only in <c>UiStrings.resx</c>.</summary>
public static class UiText
{
    private static readonly ResourceManager Manager = new("Presenta.Companion.ViewModels.UiStrings", typeof(UiText).Assembly);

    public static string Get(string key) =>
        Manager.GetString(key, CultureInfo.InvariantCulture) ?? throw new InvalidOperationException($"Missing UI string '{key}'.");

    public static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), arguments);
}

/// <summary>Runs an action on the UI thread. The WPF app wraps its dispatcher; tests run the action immediately.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>Runs actions on the calling thread. For tests.</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

/// <summary>Minimal <see cref="INotifyPropertyChanged"/> base class.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A command that runs a plain action.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();
}

/// <summary>Addresses of this computer that a physical watch on the same network could use.</summary>
public static class LocalNetwork
{
    public static IReadOnlyList<string> GetLanAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .Where(a => !a.StartsWith("169.254.", StringComparison.Ordinal))
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return Array.Empty<string>();
        }
    }
}
