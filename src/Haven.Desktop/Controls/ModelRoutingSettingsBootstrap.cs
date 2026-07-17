using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

internal static class ModelRoutingSettingsBootstrap
{
    private static readonly ConditionalWeakTable<ProviderConnectionsView, Marker> Attached = new();

    [ModuleInitializer]
    internal static void Initialize() => VisualBootstrapHost.Register(AttachVisible);

    private static void AttachVisible(Visual root)
    {
        foreach (var providerView in root.GetVisualDescendants().OfType<ProviderConnectionsView>().ToArray())
        {
            if (Attached.TryGetValue(providerView, out _)) continue;
            if (providerView.Parent is not StackPanel stack) continue;
            var index = stack.Children.IndexOf(providerView);
            if (index < 0) continue;

            stack.Children.Insert(index + 1, new ModelRoutingSettingsView());
            Attached.Add(providerView, new Marker());
        }
    }

    private sealed class Marker
    {
    }
}
