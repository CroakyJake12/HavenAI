/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/LanguageServerSettingsBootstrap.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns LanguageServerSettingsBootstrap, Marker. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

/// <summary>
/// Represents language server settings bootstrap and keeps its related state and behavior together.
/// </summary>
internal static class LanguageServerSettingsBootstrap
{
    /// <summary>
    /// Stores attached locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly ConditionalWeakTable<ModelRoutingSettingsView, Marker> Attached = new();

    /// <summary>
    /// Performs the initialize step owned by this component.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize() => VisualBootstrapHost.Register(AttachVisible);

    /// <summary>
    /// Performs the attach visible step owned by this component.
    /// </summary>
    private static void AttachVisible(Visual root)
    {
        foreach (var routingView in root.GetVisualDescendants().OfType<ModelRoutingSettingsView>().ToArray())
        {
            if (Attached.TryGetValue(routingView, out _)) continue;
            if (routingView.Parent is not StackPanel stack) continue;
            var index = stack.Children.IndexOf(routingView);
            if (index < 0) continue;

            stack.Children.Insert(index + 1, new LanguageServerSettingsView());
            Attached.Add(routingView, new Marker());
        }
    }

    /// <summary>
    /// Represents marker and keeps its related state and behavior together.
    /// </summary>
    private sealed class Marker
    {
    }
}
