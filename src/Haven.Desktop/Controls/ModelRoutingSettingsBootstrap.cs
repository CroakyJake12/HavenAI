using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

internal static class ModelRoutingSettingsBootstrap
{
    private static readonly HashSet<ProviderConnectionsView> Attached = new(ReferenceComparer.Instance);
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
                    window.LayoutUpdated += (_, _) => AttachVisible(window);
                    AttachVisible(window);
                    return;
                }
                await Task.Delay(100);
            }
        }, DispatcherPriority.Background);
    }

    private static void AttachVisible(Visual root)
    {
        foreach (var providerView in root.GetVisualDescendants().OfType<ProviderConnectionsView>().ToArray())
        {
            if (!Attached.Add(providerView)) continue;
            if (providerView.Parent is not StackPanel stack)
            {
                Attached.Remove(providerView);
                continue;
            }
            var index = stack.Children.IndexOf(providerView);
            if (index < 0)
            {
                Attached.Remove(providerView);
                continue;
            }
            stack.Children.Insert(index + 1, new ModelRoutingSettingsView());
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<ProviderConnectionsView>
    {
        public static ReferenceComparer Instance { get; } = new();
        public bool Equals(ProviderConnectionsView? x, ProviderConnectionsView? y) => ReferenceEquals(x, y);
        public int GetHashCode(ProviderConnectionsView obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
