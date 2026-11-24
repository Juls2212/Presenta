using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Presenta.Ai;
using Presenta.Companion.App.Views;
using Presenta.Companion.ViewModels;
using Presenta.Server;
using Presenta.Targeting;
using Presenta.TestSupport;

namespace Presenta.Companion.App.Tests;

/// <summary>
/// One STA thread with a WPF <see cref="Application"/> for the whole test run. Tests send work to it with <see cref="Run{T}"/>.
/// Nothing here creates a <see cref="Window"/>: controls are measured, arranged and rendered without being shown.
/// </summary>
internal static class Sta
{
    private static readonly Lazy<Dispatcher> Host = new(Start);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var name in new[] { "Tokens", "Controls" })
            {
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Presenta;component/Themes/{name}.xaml") });
            }

            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Presenta UI tests",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    public static T Run<T>(Func<T> work) => Host.Value.Invoke(work);

    public static void Run(Action work) => Host.Value.Invoke(work);

    /// <summary>Lays the element out at a size, twice, so templates and item containers exist. Never shows anything.</summary>
    public static void Realize(FrameworkElement element, double width = 1040, double height = 680)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            element.Measure(new Size(width, height));
            element.Arrange(new Rect(0, 0, width, height));
            element.UpdateLayout();
            Host.Value.Invoke(DispatcherPriority.Loaded, () => { });
        }
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    public static IEnumerable<T> All<T>(DependencyObject root)
        where T : DependencyObject => Descendants(root).OfType<T>();

    /// <summary>The name a screen reader would speak for an element (automation name, or its text).</summary>
    public static string SpokenName(UIElement element) =>
        UIElementAutomationPeer.CreatePeerForElement(element)?.GetName() ?? string.Empty;
}

/// <summary>A main view model over fake services: a fake window system, a fake clipboard, a fake AI client. No sockets are opened.</summary>
internal sealed class Rig : IDisposable
{
    public required CompanionHost Host { get; init; }

    public required MainViewModel ViewModel { get; init; }

    public required FakeWindowSystem Windows { get; init; }

    public required RecordingClipboard Clipboard { get; init; }

    public required AiRecommendationEnricher Enricher { get; init; }

    public static Rig Create(bool withTarget = true, bool withAi = true, params WindowInfo[] windows)
    {
        var system = new FakeWindowSystem();
        foreach (var window in windows)
        {
            system.Add(window);
        }

        var dispatcher = new ImmediateDispatcher();
        var clipboard = new RecordingClipboard();
        var time = new ManualTimeProvider();
        var host = new CompanionHost(new CompanionServerOptions { Port = 0, PairingCode = "329573" }, new RecordingSlideKeyboard(), new InMemoryHistoryStore(), time);
        var enricher = new AiRecommendationEnricher(new FakeAiTextClient(), AiAvailability.Available, new AiEnrichmentOptions(), time);
        var target = withTarget
            ? new TargetSelectionViewModel(new PresentationTargetService(system, new InMemoryTargetSettingsStore()), dispatcher, clipboard)
            : null;
        var ai = withAi ? new AiSectionViewModel(enricher, dispatcher) : null;
        var viewModel = new MainViewModel(host, dispatcher, lanAddresses: () => Array.Empty<string>(), target: target, ai: ai, clipboard: clipboard);
        return new Rig { Host = host, ViewModel = viewModel, Windows = system, Clipboard = clipboard, Enricher = enricher };
    }

    public ShellView CreateShell() => new() { DataContext = ViewModel };

    public void Dispose() => ViewModel.Dispose();
}

internal sealed class RecordingClipboard : IClipboard
{
    public List<string> Texts { get; } = new();

    public void SetText(string text) => Texts.Add(text);
}
