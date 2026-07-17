using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

internal static class CrossModeRetrievalBootstrap
{
    private static readonly ConditionalWeakTable<ConversationRetrievalView, Marker> Attached = new();

    [ModuleInitializer]
    internal static void Initialize() => VisualBootstrapHost.Register(AttachVisible);

    private static void AttachVisible(Visual root)
    {
        foreach (var retrievalView in root.GetVisualDescendants().OfType<ConversationRetrievalView>().ToArray())
        {
            if (Attached.TryGetValue(retrievalView, out _)) continue;
            if (retrievalView.Parent is not StackPanel stack) continue;
            var index = stack.Children.IndexOf(retrievalView);
            if (index < 0) continue;

            stack.Children.Insert(index + 1, new CrossModeRetrievalView());
            Attached.Add(retrievalView, new Marker());
        }
    }

    private sealed class Marker
    {
    }
}
