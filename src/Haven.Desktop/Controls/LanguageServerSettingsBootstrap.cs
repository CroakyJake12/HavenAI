using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

internal static class LanguageServerSettingsBootstrap
{
    private static readonly ConditionalWeakTable<ModelRoutingSettingsView, Marker> Attached = new();

    [ModuleInitializer]
    internal static void Initialize() => VisualBootstrapHost.Register(AttachVisible);

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

    private sealed class Marker
    {
    }
}
