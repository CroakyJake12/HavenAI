using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Creates normal Haven windows and chrome-free pop-ups while moving the same tab session.</summary>
public sealed class WorkspaceWindowService(
    IServiceProvider services,
    WorkspaceSessionCoordinator sessions,
    NotificationService notifications)
{
    private readonly List<Window> _windows = [];

    public void OpenInNewWindow(MainView source, WorkspaceTabViewModel tab)
    {
        var moved = source.DetachTabForMove(tab);
        var shell = services.GetRequiredService<MainView>();
        shell.ApplyEdition(source.Edition);
        shell.AttachTransferredTab(moved, replaceExisting: true);
        sessions.Register(shell, WorkspaceWindowKind.Normal);
        var window = CreateMainWindow();
        window.DataContext = shell;
        window.Title = moved.Title;

        window.Closed += (_, _) => { _windows.Remove(window); sessions.Unregister(shell); };
        _windows.Add(window);
        window.Show();
        _ = InitializeSecondaryShellAsync(shell);
    }

    public void OpenInPopUp(MainView source, WorkspaceTabViewModel tab)
    {
        var moved = source.DetachTabForMove(tab);
        var windowId = Guid.NewGuid();
        var layoutId = Guid.NewGuid();
        var paneId = Guid.NewGuid();
        var host = new ContentControl { Content = moved.Page };
        AutomationProperties.SetName(host, $"{moved.Title} pop-up surface");
        var window = new Window
        {
            Title = moved.Title,
            Width = 960,
            Height = 720,
            MinWidth = 420,
            MinHeight = 320,
            Content = host,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        sessions.RegisterPopUp(
            windowId,
            () => source.CreateTabSnapshot(moved),
            () => new WorkspaceWindowSnapshot(
                windowId,
                WorkspaceWindowKind.PopUp,
                new WorkspaceLayoutSnapshot(layoutId, WorkspaceLayoutKind.Single, SplitOrientation.Vertical, 1d,
                    [new WorkspacePaneSnapshot(paneId, moved.SessionId, 0)]),
                [moved.SessionId],
                moved.SessionId,
                SerializeWindowBounds(window),
                DateTimeOffset.UtcNow));
        window.PositionChanged += (_, _) => sessions.QueueSave();
        window.SizeChanged += (_, _) => sessions.QueueSave();
        window.Closed += (_, _) =>
        {
            sessions.UnregisterPopUp(windowId);
            host.Content = null;
            _windows.Remove(window);
            if (!source.IsDisposed) source.AttachTransferredTab(moved, replaceExisting: false);
            else
            {
                var shell = services.GetRequiredService<MainView>();
                shell.ApplyEdition(source.Edition);
                shell.AttachTransferredTab(moved, replaceExisting: true);
                sessions.Register(shell, WorkspaceWindowKind.Normal);
                var fallback = CreateMainWindow();
                fallback.DataContext = shell;
                fallback.Title = moved.Title;
                fallback.Closed += (_, _) => { _windows.Remove(fallback); sessions.Unregister(shell); };
                _windows.Add(fallback);
                fallback.Show();
                _ = InitializeSecondaryShellAsync(shell);
            }
        };
        _windows.Add(window);
        if (TopLevel.GetTopLevel(source) is Window owner) window.Show(owner); else window.Show();
    }

    public async Task RestoreAdditionalWindowsAsync(
        WorkspaceSessionSnapshot snapshot,
        HavenShellEdition edition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach (var saved in snapshot.Windows.Where(item => item.Kind != WorkspaceWindowKind.Main))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (saved.OrderedTabIds.Count == 0) continue;

            var shell = services.GetRequiredService<MainView>();
            shell.ApplyEdition(edition);
            try
            {
                await shell.RestoreWorkspaceWindowAsync(snapshot, saved, cancellationToken);
            }
            catch
            {
                shell.Dispose();
                continue;
            }

            if (shell.SelectedTab is not { } selected)
            {
                shell.Dispose();
                continue;
            }

            if (saved.Kind == WorkspaceWindowKind.PopUp)
            {
                var moved = shell.DetachTabForMove(selected);
                shell.Dispose();
                RestorePersistedPopUp(moved, saved);
                continue;
            }

            sessions.Register(shell, WorkspaceWindowKind.Normal, queueSave: false);
            var window = CreateMainWindow();
            window.DataContext = shell;
            window.Title = selected.Title;
            ApplyWindowBounds(window, saved.BoundsJson);

            window.Closed += (_, _) => { _windows.Remove(window); sessions.Unregister(shell); };
            _windows.Add(window);
            window.Show();
            _ = InitializeSecondaryShellAsync(shell);
        }
    }
    private void RestorePersistedPopUp(WorkspaceTabViewModel moved, WorkspaceWindowSnapshot saved)
    {
        var host = new ContentControl { Content = moved.Page };
        AutomationProperties.SetName(host, $"{moved.Title} pop-up surface");
        var window = new Window
        {
            Title = moved.Title,
            Width = 960,
            Height = 720,
            MinWidth = 420,
            MinHeight = 320,
            Content = host,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        ApplyWindowBounds(window, saved.BoundsJson);
        sessions.RegisterPopUp(
            saved.Id,
            () => MainView.CreateDetachedTabSnapshot(moved),
            () => new WorkspaceWindowSnapshot(
                saved.Id,
                WorkspaceWindowKind.PopUp,
                saved.Layout,
                [moved.SessionId],
                moved.SessionId,
                SerializeWindowBounds(window),
                DateTimeOffset.UtcNow));

        window.PositionChanged += (_, _) => sessions.QueueSave();
        window.SizeChanged += (_, _) => sessions.QueueSave();
        window.Closed += (_, _) =>
        {
            sessions.UnregisterPopUp(saved.Id);
            host.Content = null;
            _windows.Remove(window);
            moved.Dispose();
        };
        _windows.Add(window);
        window.Show();
    }

    internal static void ApplyWindowBounds(Window window, string? json)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var bounds = JsonSerializer.Deserialize<WindowBoundsState>(json);
            if (bounds is null ||
                !double.IsFinite(bounds.Width) ||
                !double.IsFinite(bounds.Height) ||
                bounds.Width <= 0 ||
                bounds.Height <= 0)
                return;

            var minimumWidth = double.IsFinite(window.MinWidth) && window.MinWidth > 0 ? window.MinWidth : 320d;
            var minimumHeight = double.IsFinite(window.MinHeight) && window.MinHeight > 0 ? window.MinHeight : 240d;
            window.Width = Math.Clamp(bounds.Width, minimumWidth, 1600d);
            window.Height = Math.Clamp(bounds.Height, minimumHeight, 1000d);

            if (Math.Abs(bounds.X) <= 32768 && Math.Abs(bounds.Y) <= 32768)
                window.Position = new PixelPoint(bounds.X, bounds.Y);
        }
        catch (JsonException)
        {
            // Old or corrupt geometry falls back to normal startup sizing.
        }
    }
    private async Task InitializeSecondaryShellAsync(MainView shell)
    {
        try
        {
            await shell.InitializeSecondaryWindowAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            notifications.Show(
                "Window initialization failed",
                SensitiveTextRedactor.Redact(ex.Message),
                ToastKind.Error,
                TimeSpan.FromSeconds(12));
        }
    }

    private static string SerializeWindowBounds(Window window) => JsonSerializer.Serialize(new WindowBoundsState(
        window.Position.X, window.Position.Y, window.Width, window.Height));

    private sealed record WindowBoundsState(int X, int Y, double Width, double Height);

    private MainWindow CreateMainWindow()
    {
#if ANDROID
        return new MainWindow();
#else
        return new MainWindow(services.GetRequiredService<UserPreferencesService>());
#endif
    }
}
