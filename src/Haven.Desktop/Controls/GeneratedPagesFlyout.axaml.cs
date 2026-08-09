using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Core;

namespace Haven.Desktop.Controls;

/// <summary>
/// AXAML-defined generated pages flyout. Shows a list of generated pages.
/// </summary>
public sealed partial class GeneratedPagesFlyout : UserControl
{
    public GeneratedPagesFlyout()
    {
        InitializeComponent();
    }

    public event EventHandler<GeneratedPageDefinition>? PageSelected;

    public void SetPages(IReadOnlyList<GeneratedPageDefinition> pages)
    {
        PagesList.Children.Clear();
        foreach (var page in pages)
        {
            var pageButton = new HavenButton
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = page.Title, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = page.Description, FontSize = 10, Opacity = 0.7, TextWrapping = TextWrapping.Wrap }
                    }
                }
            };
            pageButton.Classes.Add("sidebar");
            pageButton.Click += (_, _) => PageSelected?.Invoke(this, page);
            PagesList.Children.Add(pageButton);
        }
    }
}
