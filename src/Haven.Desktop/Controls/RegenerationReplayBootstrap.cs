using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

internal static class RegenerationReplayBootstrap
{
    private static readonly ConditionalWeakTable<ConversationProductionToolbarView, Marker> Installed = new();

    [ModuleInitializer]
    internal static void Initialize() => VisualBootstrapHost.Register(InstallVisible);

    private static void InstallVisible(Visual root)
    {
        foreach (var toolbar in root.GetVisualDescendants().OfType<ConversationProductionToolbarView>())
        {
            if (Installed.TryGetValue(toolbar, out _)) continue;
            toolbar.InstallSafeRegenerationHandler();
            Installed.Add(toolbar, new Marker());
        }
    }

    private sealed class Marker
    {
    }
}
