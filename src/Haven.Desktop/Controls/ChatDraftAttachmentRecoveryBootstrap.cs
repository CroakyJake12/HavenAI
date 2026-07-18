/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/ChatDraftAttachmentRecoveryBootstrap.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns ChatDraftAttachmentRecoveryBootstrap, RecoveryState. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

/// <summary>
/// Represents chat draft attachment recovery bootstrap and keeps its related state and behavior together.
/// </summary>
internal static class ChatDraftAttachmentRecoveryBootstrap
{
    /// <summary>
    /// Stores states locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly ConditionalWeakTable<ChatView, RecoveryState> States = new();

    /// <summary>
    /// Performs the initialize step owned by this component.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize() => VisualBootstrapHost.Register(RecoverVisible);

    /// <summary>
    /// Performs the recover visible step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs recover async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Represents recovery state and keeps its related state and behavior together.
    /// </summary>
    private sealed class RecoveryState
    {
        /// <summary>
        /// Runs running while preserving the surrounding cancellation and error-handling contract.
        /// </summary>
        public bool Running { get; set; }
        /// <summary>
        /// Gets or updates completed, the bindable or domain state represented by this property.
        /// </summary>
        public bool Completed { get; set; }
    }
}
