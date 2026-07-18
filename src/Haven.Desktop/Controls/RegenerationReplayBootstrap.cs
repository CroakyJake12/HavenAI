/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/RegenerationReplayBootstrap.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns RegenerationReplayBootstrap, Marker. Read the type and member comments below as a map of each responsibility.
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
/// Represents regeneration replay bootstrap and keeps its related state and behavior together.
/// </summary>
internal static class RegenerationReplayBootstrap
{
    /// <summary>
    /// Stores installed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly ConditionalWeakTable<ConversationProductionToolbarView, Marker> Installed = new();

    /// <summary>
    /// Performs the initialize step owned by this component.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize() => VisualBootstrapHost.Register(InstallVisible);

    /// <summary>
    /// Performs the install visible step owned by this component.
    /// </summary>
    private static void InstallVisible(Visual root)
    {
        foreach (var toolbar in root.GetVisualDescendants().OfType<ConversationProductionToolbarView>())
        {
            if (Installed.TryGetValue(toolbar, out _)) continue;
            toolbar.InstallSafeRegenerationHandler();
            Installed.Add(toolbar, new Marker());
        }
    }

    /// <summary>
    /// Represents marker and keeps its related state and behavior together.
    /// </summary>
    private sealed class Marker
    {
    }
}
