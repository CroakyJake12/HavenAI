using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

internal static class BrowserSafetyBootstrap
{
    private static readonly HashSet<BrowserView> Attached = new(ReferenceComparer.Instance);
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
        foreach (var browserView in root.GetVisualDescendants().OfType<BrowserView>().ToArray())
        {
            if (!Attached.Add(browserView)) continue;
            if (browserView.Content is not Grid grid)
            {
                Attached.Remove(browserView);
                continue;
            }

            var button = new Button
            {
                Content = "Browser safety",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 470, 0),
                Padding = new Thickness(9, 4),
                FontSize = 10,
                ToolTip = "Review model-requested form submissions and downloads"
            };
            button.Classes.Add("ghost");
            Grid.SetRow(button, 3);
            Panel.SetZIndex(button, 20);
            var flyout = new Flyout { Content = new BrowserSafetyView() };
            FlyoutBase.SetAttachedFlyout(button, flyout);
            button.Click += (_, _) => FlyoutBase.ShowAttachedFlyout(button);
            grid.Children.Add(button);
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<BrowserView>
    {
        public static ReferenceComparer Instance { get; } = new();
        public bool Equals(BrowserView? x, BrowserView? y) => ReferenceEquals(x, y);
        public int GetHashCode(BrowserView obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
