/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/ConversationProductionToolbarView.RegenerationReplay.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns ConversationProductionToolbarView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Desktop.Views;

/// <summary>
/// Represents conversation production toolbar view and keeps its related state and behavior together.
/// </summary>
public sealed partial class ConversationProductionToolbarView
{
    /// <summary>
    /// Stores safe regeneration handler installed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    // Message regeneration now lives in ChatView's message-local action flyout.
    // Keep this compatibility hook because older bootstrap code calls it while
    // walking the visual tree; removing the member would break that startup path.
    internal void InstallSafeRegenerationHandler() { }
}
