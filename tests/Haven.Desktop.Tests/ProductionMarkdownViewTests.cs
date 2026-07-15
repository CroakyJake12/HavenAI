using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Tests;

public sealed class ProductionMarkdownViewTests
{
    [AvaloniaFact]
    public void RendersHeadingsListsTablesCodeAndMathWithoutWebContent()
    {
        var view = new ProductionMarkdownView
        {
            Text = """
                # Heading

                - bullet
                - [x] complete

                | Name | Value |
                | --- | --- |
                | A | 1 |

                Inline $\\alpha + \\beta$.

                $$
                \\frac{1}{2} \\leq 1
                $$

                ```csharp
                Console.WriteLine("Hello");
                ```
                """
        };

        var controls = Descendants(view).ToArray();
        Assert.Contains(controls.OfType<TextBlock>(), item => item.Text == "Heading");
        Assert.Contains(controls.OfType<CheckBox>(), item => item.IsChecked == true && item.IsEnabled == false);
        Assert.Contains(controls.OfType<Button>(), item => Equals(item.Content, "Copy"));
        Assert.Contains(controls.OfType<Button>(), item => Equals(item.Content, "Ask to run"));
        Assert.Contains(controls.OfType<Button>(), item => Equals(item.Content, "Ask to apply"));
        Assert.Contains(controls.OfType<SelectableTextBlock>(), item => item.Text?.Contains('½') == true || item.Text?.Contains('⁄') == true);
        Assert.DoesNotContain(controls, item => item.GetType().Name.Contains("WebView", StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public void AskToRunRaisesExplicitActionInsteadOfExecutingCode()
    {
        var view = new ProductionMarkdownView { Text = "```powershell\nWrite-Output test\n```" };
        MarkdownCodeActionRequest? request = null;
        view.CodeActionRequested += value => request = value;
        var button = Descendants(view).OfType<Button>().Single(item => Equals(item.Content, "Ask to run"));

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.NotNull(request);
        Assert.Equal(MarkdownCodeAction.AskToRun, request!.Action);
        Assert.Equal("powershell", request.Language);
        Assert.Contains("Write-Output test", request.Code);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        switch (root)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                    foreach (var descendant in Descendants(child)) yield return descendant;
                break;
            case ContentControl { Content: Control child }:
                foreach (var descendant in Descendants(child)) yield return descendant;
                break;
            case Decorator { Child: { } child }:
                foreach (var descendant in Descendants(child)) yield return descendant;
                break;
        }
    }
}
