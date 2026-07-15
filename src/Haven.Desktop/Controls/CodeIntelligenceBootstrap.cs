using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

internal static class CodeIntelligenceBootstrap
{
    private static readonly HashSet<CrossModeRetrievalView> Attached = new(ReferenceComparer.Instance);
    private static bool _scheduled;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (_scheduled) return;
        _scheduled = true;
        Dispatcher.UIThread.Post(async () =>
        {
            for (var attempt = 0; attempt < 120; attempt++)
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
                {
                    window.LayoutUpdated += (_, _) => AttachVisible(window);
                    AttachVisible(window);
                    return;
                }
                await Task.Delay(100);
            }
        }, DispatcherPriority.Background);
    }

    private static void AttachVisible(Visual root)
    {
        foreach (var retrievalView in root.GetVisualDescendants().OfType<CrossModeRetrievalView>().ToArray())
        {
            if (!Attached.Add(retrievalView)) continue;
            if (retrievalView.Parent is not StackPanel stack)
            {
                Attached.Remove(retrievalView);
                continue;
            }
            var index = stack.Children.IndexOf(retrievalView);
            if (index < 0)
            {
                Attached.Remove(retrievalView);
                continue;
            }
            stack.Children.Insert(index + 1, new CodeIntelligenceView());
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<CrossModeRetrievalView>
    {
        public static ReferenceComparer Instance { get; } = new();
        public bool Equals(CrossModeRetrievalView? x, CrossModeRetrievalView? y) => ReferenceEquals(x, y);
        public int GetHashCode(CrossModeRetrievalView obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
