/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/VisualBootstrapHost.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns VisualBootstrapHost. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Haven.Desktop.Controls;

/// <summary>
/// Hosts continuation-era visual augmentations until they can be moved into their
/// owning XAML views. It discovers the main window without async-void callbacks,
/// invokes each augmentation independently, and does not retain augmented views.
/// </summary>
internal static class VisualBootstrapHost
{
    /// <summary>
    /// Stores sync locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly object Sync = new();
    /// <summary>
    /// Stores attachments locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly List<Action<Visual>> Attachments = [];
    /// <summary>
    /// Stores startup timer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static DispatcherTimer? _startupTimer;
    /// <summary>
    /// Stores window locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static Window? _window;
    /// <summary>
    /// Stores scheduled locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static bool _scheduled;

    /// <summary>
    /// Performs the register step owned by this component.
    /// </summary>
    public static void Register(Action<Visual> attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        Window? currentWindow;
        var schedule = false;
        lock (Sync)
        {
            Attachments.Add(attachment);
            currentWindow = _window;
            if (!_scheduled)
            {
                _scheduled = true;
                schedule = true;
            }
        }

        if (currentWindow is not null)
        {
            Dispatcher.UIThread.Post(
                () => InvokeAttachment(attachment, currentWindow),
                DispatcherPriority.Background);
        }
        else if (schedule)
        {
            Dispatcher.UIThread.Post(StartPolling, DispatcherPriority.Background);
        }
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
        TryInstallWindow();
    }

    /// <summary>
    /// Handles the startup tick event raised by the UI or runtime.
    /// </summary>
    private static void OnStartupTick(object? sender, EventArgs e) => TryInstallWindow();

    /// <summary>
    /// Attempts to install window and reports the result without using failure for normal control flow.
    /// </summary>
    private static void TryInstallWindow()
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
                {
                    MainWindow: { } mainWindow
                }) return;

            InstallWindow(mainWindow);
            StopPolling();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Visual bootstrap discovery failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Performs the install window step owned by this component.
    /// </summary>
    private static void InstallWindow(Window window)
    {
        Window? previous;
        lock (Sync)
        {
            previous = _window;
            if (ReferenceEquals(previous, window))
            {
                InvokeAll(window);
                return;
            }
            _window = window;
        }

        if (previous is not null)
            previous.LayoutUpdated -= OnWindowLayoutUpdated;
        window.LayoutUpdated -= OnWindowLayoutUpdated;
        window.LayoutUpdated += OnWindowLayoutUpdated;
        InvokeAll(window);
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
        if (sender is Visual root) InvokeAll(root);
    }

    /// <summary>
    /// Performs the invoke all step owned by this component.
    /// </summary>
    private static void InvokeAll(Visual root)
    {
        Action<Visual>[] attachments;
        lock (Sync)
            attachments = [.. Attachments];

        foreach (var attachment in attachments)
            InvokeAttachment(attachment, root);
    }

    /// <summary>
    /// Performs the invoke attachment step owned by this component.
    /// </summary>
    private static void InvokeAttachment(Action<Visual> attachment, Visual root)
    {
        try { attachment(root); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Visual bootstrap attachment failed: " + ex.Message);
        }
    }
}
