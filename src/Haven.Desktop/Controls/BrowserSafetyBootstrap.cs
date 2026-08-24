/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/BrowserSafetyBootstrap.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns BrowserSafetyBootstrap, AttachmentMarker. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

/// <summary>
/// Represents browser safety bootstrap and keeps its related state and behavior together.
/// </summary>
internal static class BrowserSafetyBootstrap
{
    /// <summary>
    /// Stores attached locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly ConditionalWeakTable<BrowserView, AttachmentMarker> Attached = new();
    /// <summary>
    /// Stores startup timer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static DispatcherTimer? _startupTimer;
    /// <summary>
    /// Stores scheduled locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static bool _scheduled;

    /// <summary>
    /// Performs the initialize step owned by this component.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (_scheduled) return;
        _scheduled = true;
        Dispatcher.UIThread.Post(StartPolling, DispatcherPriority.Background);
    }

    /// <summary>
    /// Performs the start polling step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Handles the startup tick event raised by the UI or runtime.
    /// </summary>
    private static void OnStartupTick(object? sender, EventArgs e) => TryAttachMainWindow();

    /// <summary>
    /// Attempts to attach main window and reports the result without using failure for normal control flow.
    /// </summary>
    private static void TryAttachMainWindow()
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
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

    /// <summary>
    /// Performs the stop polling step owned by this component.
    /// </summary>
    private static void StopPolling()
    {
        if (_startupTimer is null) return;
        _startupTimer.Stop();
        _startupTimer.Tick -= OnStartupTick;
        _startupTimer = null;
    }

    /// <summary>
    /// Handles the window layout updated event raised by the UI or runtime.
    /// </summary>
    private static void OnWindowLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is Visual root) AttachVisible(root);
    }

    /// <summary>
    /// Performs the attach visible step owned by this component.
    /// </summary>
    private static void AttachVisible(Visual root)
    {
        foreach (var browserView in root.GetVisualDescendants().OfType<BrowserView>().ToArray())
        {
            if (Attached.TryGetValue(browserView, out _)) continue;
            if (browserView.Content is not Grid grid) continue;

            var button = new HavenButton
            {
                Content = "Browser safety",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 470, 0),
                Padding = new Thickness(9, 4),
                FontSize = 10,
                Flyout = new HavenAdaptivePopup { Content = new BrowserSafetyView() },
                ZIndex = 20
            };
            ToolTip.SetTip(button, "Review model-requested form submissions and downloads");
            button.Classes.Add("ghost");
            Grid.SetRow(button, 3);
            grid.Children.Add(button);
            Attached.Add(browserView, new AttachmentMarker());
        }
    }

    /// <summary>
    /// Represents attachment marker and keeps its related state and behavior together.
    /// </summary>
    private sealed class AttachmentMarker
    {
    }
}
