using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

internal static class RegenerationReplayBootstrap
{
    private static readonly ConditionalWeakTable<ConversationProductionToolbarView, Marker> Installed = new();
    private static bool _scheduled;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (_scheduled) return;
        _scheduled = true;
        Dispatcher.UIThread.Post(async () =>
        {
            for (var attempt = 0; attempt < 120; attempt++)
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
                {
                    window.LayoutUpdated += (_, _) => InstallVisible(window);
                    InstallVisible(window);
                    return;
                }
                await Task.Delay(100);
            }
        }, DispatcherPriority.Background);
    }

    private static void InstallVisible(Avalonia.Visual root)
    {
        foreach (var toolbar in root.GetVisualDescendants().OfType<ConversationProductionToolbarView>())
        {
            if (Installed.TryGetValue(toolbar, out _)) continue;
            toolbar.InstallSafeRegenerationHandler();
            Installed.Add(toolbar, new Marker());
        }
    }

    private sealed class Marker;
}
