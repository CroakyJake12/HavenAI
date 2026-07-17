using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

internal static class ChatDraftAttachmentRecoveryBootstrap
{
    private static readonly ConditionalWeakTable<ChatView, RecoveryState> States = new();

    [ModuleInitializer]
    internal static void Initialize() => VisualBootstrapHost.Register(RecoverVisible);

    private static void RecoverVisible(Visual root)
    {
        foreach (var chat in root.GetVisualDescendants().OfType<ChatView>())
        {
            var state = States.GetValue(chat, static _ => new RecoveryState());
            if (state.Running || state.Completed) continue;
            state.Running = true;
            _ = RecoverAsync(chat, state);
        }
    }

    private static async Task RecoverAsync(ChatView chat, RecoveryState state)
    {
        try
        {
            await chat.RecoverDraftAttachmentsAsync(CancellationToken.None);
            state.Completed = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine("Draft attachment recovery failed: " + ex.Message);
        }
        finally
        {
            state.Running = false;
        }
    }

    private sealed class RecoveryState
    {
        public bool Running { get; set; }
        public bool Completed { get; set; }
    }
}
