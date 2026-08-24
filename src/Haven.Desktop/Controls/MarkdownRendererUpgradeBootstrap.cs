/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/MarkdownRendererUpgradeBootstrap.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns MarkdownRendererUpgradeBootstrap, Marker. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

/// <summary>
/// Represents markdown renderer upgrade bootstrap and keeps its related state and behavior together.
/// </summary>
internal static class MarkdownRendererUpgradeBootstrap
{
    /// <summary>
    /// Stores upgraded locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly ConditionalWeakTable<MarkdownView, Marker> Upgraded = new();

    /// <summary>
    /// Performs the initialize step owned by this component.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize() => VisualBootstrapHost.Register(UpgradeVisibleRenderers);

    /// <summary>
    /// Performs the upgrade visible renderers step owned by this component.
    /// </summary>
    private static void UpgradeVisibleRenderers(Visual root)
    {
        foreach (var legacy in root.GetVisualDescendants().OfType<MarkdownView>().ToArray())
        {
            if (Upgraded.TryGetValue(legacy, out _)) continue;
            var replacement = new ProductionMarkdownView
            {
                DataContext = legacy.DataContext,
                HorizontalAlignment = legacy.HorizontalAlignment,
                VerticalAlignment = legacy.VerticalAlignment,
                Margin = legacy.Margin,
                MinWidth = legacy.MinWidth,
                MaxWidth = legacy.MaxWidth
            };
            replacement.Bind(ProductionMarkdownView.TextProperty, new Binding("Content"));
            if (!Replace(legacy, replacement)) continue;
            Upgraded.Add(legacy, new Marker());
        }
    }

    /// <summary>
    /// Performs the replace step owned by this component.
    /// </summary>
    private static bool Replace(Control legacy, Control replacement)
    {
        switch (legacy.Parent)
        {
            case Panel panel:
            {
                var index = panel.Children.IndexOf(legacy);
                if (index < 0) return false;
                panel.Children.RemoveAt(index);
                panel.Children.Insert(index, replacement);
                return true;
            }
            case ContentControl content when ReferenceEquals(content.Content, legacy):
                content.Content = replacement;
                return true;
            case Decorator decorator when ReferenceEquals(decorator.Child, legacy):
                decorator.Child = replacement;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Represents marker and keeps its related state and behavior together.
    /// </summary>
    private sealed class Marker
    {
    }
}
