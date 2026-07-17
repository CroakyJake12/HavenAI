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
    private static readonly object Sync = new();
    private static readonly List<Action<Visual>> Attachments = [];
    private static DispatcherTimer? _startupTimer;
    private static Window? _window;
    private static bool _scheduled;

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

    private static void OnStartupTick(object? sender, EventArgs e) => TryInstallWindow();

    private static void TryInstallWindow()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
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

    private static void StopPolling()
    {
        if (_startupTimer is null) return;
        _startupTimer.Stop();
        _startupTimer.Tick -= OnStartupTick;
        _startupTimer = null;
    }

    private static void OnWindowLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is Visual root) InvokeAll(root);
    }

    private static void InvokeAll(Visual root)
    {
        Action<Visual>[] attachments;
        lock (Sync)
            attachments = [.. Attachments];

        foreach (var attachment in attachments)
            InvokeAttachment(attachment, root);
    }

    private static void InvokeAttachment(Action<Visual> attachment, Visual root)
    {
        try { attachment(root); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Visual bootstrap attachment failed: " + ex.Message);
        }
    }
}
