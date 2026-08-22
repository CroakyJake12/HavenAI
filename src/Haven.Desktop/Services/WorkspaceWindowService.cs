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
                null,
                DateTimeOffset.UtcNow));
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

    private MainWindow CreateMainWindow()
    {
#if ANDROID
        return new MainWindow();
#else
        return new MainWindow(services.GetRequiredService<UserPreferencesService>());
#endif
    }
}
