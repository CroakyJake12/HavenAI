using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

internal static class BrowserSafetyBootstrap
{
    private static readonly ConditionalWeakTable<BrowserView, AttachmentMarker> Attached = new();
    private static DispatcherTimer? _startupTimer;
    private static bool _scheduled;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (_scheduled) return;
        _scheduled = true;
        Dispatcher.UIThread.Post(StartPolling, DispatcherPriority.Background);
    }

    private static void StartPolling()
    {
        if (_startupTimer is not null) return;
        _startupTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _startupTimer.Tick += OnStartupTick;
        _startupTimer.Start();
        TryAttachMainWindow();
    }

    private static void OnStartupTick(object? sender, EventArgs e) => TryAttachMainWindow();

    private static void TryAttachMainWindow()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
                {
                    MainWindow: { } window
                }) return;

            StopPolling();
            window.LayoutUpdated -= OnWindowLayoutUpdated;
            window.LayoutUpdated += OnWindowLayoutUpdated;
            AttachVisible(window);
        }
        catch (Exception)
        {
            // The real Browser view remains usable if optional safety-surface
            // injection cannot attach during a transient startup layout.
        }
    }

    private static void StopPolling()
    {
        if (_startupTimer is null) return;
        _startupTimer.Stop();
        _startupTimer.Tick -= OnStartupTick;
        _startupTimer = null;
    }

    private static void OnWindowLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is Visual root) AttachVisible(root);
    }

    private static void AttachVisible(Visual root)
    {
        foreach (var browserView in root.GetVisualDescendants().OfType<BrowserView>().ToArray())
        {
            if (Attached.TryGetValue(browserView, out _)) continue;
            if (browserView.Content is not Grid grid) continue;

            var button = new Button
            {
                Content = "Browser safety",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 470, 0),
                Padding = new Thickness(9, 4),
                FontSize = 10,
                Flyout = new Flyout { Content = new BrowserSafetyView() }
            };
            ToolTip.SetTip(button, "Review model-requested form submissions and downloads");
            button.Classes.Add("ghost");
            Grid.SetRow(button, 3);
            Panel.SetZIndex(button, 20);
            grid.Children.Add(button);
            Attached.Add(browserView, new AttachmentMarker());
        }
    }

    private sealed class AttachmentMarker
    {
    }
}
