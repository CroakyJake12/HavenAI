/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/ProductionMarkdownViewTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ProductionMarkdownViewTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents production markdown view tests and keeps its related state and behavior together.
/// </summary>
public sealed class ProductionMarkdownViewTests
{
    /// <summary>
    /// Performs the renders headings lists tables code and math without web content step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the ask to run raises explicit action instead of executing code step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the descendants step owned by this component.
    /// </summary>
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
