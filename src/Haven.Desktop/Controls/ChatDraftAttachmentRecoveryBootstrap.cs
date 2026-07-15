using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

internal static class ChatDraftAttachmentRecoveryBootstrap
{
    private static readonly ConditionalWeakTable<ChatView, RecoveryState> States = new();
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
                    window.LayoutUpdated += (_, _) => RecoverVisible(window);
                    RecoverVisible(window);
                    return;
                }
                await Task.Delay(100);
            }
        }, DispatcherPriority.Background);
    }

    private static void RecoverVisible(Visual root)
    {
        foreach (var chat in root.GetVisualDescendants().OfType<ChatView>())
        {
            var state = States.GetValue(chat, static _ => new RecoveryState());
            if (state.Running) continue;
            state.Running = true;
            _ = RecoverAsync(chat, state);
        }
    }

    private static async Task RecoverAsync(ChatView chat, RecoveryState state)
    {
        try { await chat.RecoverDraftAttachmentsAsync(CancellationToken.None); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine("Draft attachment recovery failed: " + ex.Message);
        }
        finally { state.Running = false; }
    }

    private sealed class RecoveryState
    {
        public bool Running { get; set; }
    }
}
