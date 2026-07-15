using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

internal static class LanguageServerSettingsBootstrap
{
    private static readonly HashSet<ModelRoutingSettingsView> Attached = new(ReferenceComparer.Instance);
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
        foreach (var routingView in root.GetVisualDescendants().OfType<ModelRoutingSettingsView>().ToArray())
        {
            if (!Attached.Add(routingView)) continue;
            if (routingView.Parent is not StackPanel stack)
            {
                Attached.Remove(routingView);
                continue;
            }
            var index = stack.Children.IndexOf(routingView);
            if (index < 0)
            {
                Attached.Remove(routingView);
                continue;
            }
            stack.Children.Insert(index + 1, new LanguageServerSettingsView());
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<ModelRoutingSettingsView>
    {
        public static ReferenceComparer Instance { get; } = new();
        public bool Equals(ModelRoutingSettingsView? x, ModelRoutingSettingsView? y) => ReferenceEquals(x, y);
        public int GetHashCode(ModelRoutingSettingsView obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
