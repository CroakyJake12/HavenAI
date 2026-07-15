using Avalonia.VisualTree;
using Haven.Desktop.ViewModels;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class ConversationProductionToolbarView
{
    private bool _safeRegenerationHandlerInstalled;

    internal void InstallSafeRegenerationHandler()
    {
        if (_safeRegenerationHandlerInstalled) return;
        _safeRegenerationHandlerInstalled = true;
        _messageTools.RegenerationRequested -= OnRegenerationRequested;
        _messageTools.RegenerationRequested += ReplayRegenerationAsync;
    }

    private async void ReplayRegenerationAsync(string prompt)
    {
        if (App.Services is null || this.FindAncestorOfType<ChatView>()?.DataContext is not ChatPageViewModel chat) return;
        try
        {
            var replay = ActivatorUtilities.CreateInstance<RegenerationReplayService>(App.Services);
            await replay.PrepareUserReplayAsync(chat.ConversationId, prompt, CancellationToken.None);
            await chat.LoadConversationAsync(chat.ConversationId, CancellationToken.None);
            chat.Composer = prompt;
            if (chat.SendCommand.CanExecute(null)) chat.SendCommand.Execute(null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            System.Diagnostics.Debug.WriteLine("Regeneration replay failed: " + ex.Message);
        }
    }
}
