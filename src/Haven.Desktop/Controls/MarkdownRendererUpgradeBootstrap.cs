using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

internal static class MarkdownRendererUpgradeBootstrap
{
    private static readonly ConditionalWeakTable<MarkdownView, Marker> Upgraded = new();

    [ModuleInitializer]
    internal static void Initialize() => VisualBootstrapHost.Register(UpgradeVisibleRenderers);

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
            replacement.CodeActionRequested += request => HandleCodeAction(replacement, request);
            if (!Replace(legacy, replacement)) continue;
            Upgraded.Add(legacy, new Marker());
        }
    }

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

    private static void HandleCodeAction(Control source, MarkdownCodeActionRequest request)
    {
        if (request.Action == MarkdownCodeAction.Copy) return;
        if (source.FindAncestorOfType<ChatView>()?.DataContext is not ChatPageViewModel chat) return;
        var language = string.IsNullOrWhiteSpace(request.Language) ? "code" : request.Language;
        var instruction = request.Action == MarkdownCodeAction.AskToRun
            ? $"Run this {language} code using the appropriate permission-gated Haven tool. Explain the command before execution and report the real exit code and output:\n\n```{request.Language}\n{request.Code}\n```"
            : $"Apply this {language} code to the currently selected project only after checking the target file and asking for approval where required. Show the exact proposed edit first:\n\n```{request.Language}\n{request.Code}\n```";
        chat.Composer = string.IsNullOrWhiteSpace(chat.Composer)
            ? instruction
            : chat.Composer.TrimEnd() + "\n\n" + instruction;
    }

    private sealed class Marker
    {
    }
}
