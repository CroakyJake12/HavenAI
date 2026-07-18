/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/ConversationProductionToolbarView.RegenerationReplay.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns ConversationProductionToolbarView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.VisualTree;
using Haven.Desktop.ViewModels;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents conversation production toolbar view and keeps its related state and behavior together.
/// </summary>
public sealed partial class ConversationProductionToolbarView
{
    /// <summary>
    /// Stores safe regeneration handler installed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _safeRegenerationHandlerInstalled;

    /// <summary>
    /// Performs the install safe regeneration handler step owned by this component.
    /// </summary>
    internal void InstallSafeRegenerationHandler()
    {
        if (_safeRegenerationHandlerInstalled) return;
        _safeRegenerationHandlerInstalled = true;
        _messageTools.RegenerationRequested -= OnRegenerationRequested;
        _messageTools.RegenerationRequested += ReplayRegenerationAsync;
    }

    /// <summary>
    /// Performs replay regeneration asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
