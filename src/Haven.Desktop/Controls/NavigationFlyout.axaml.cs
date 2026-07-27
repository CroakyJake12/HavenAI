using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Core;

namespace Haven.Desktop.Controls;

/// <summary>
/// AXAML-defined navigation flyout. Shows a list of navigation entries.
/// </summary>
public sealed partial class NavigationFlyout : UserControl
{
    public NavigationFlyout()
    {
        InitializeComponent();
    }

    public void SetEntries(IReadOnlyList<NavigationEntry> entries)
    {
        EntriesPanel.Children.Clear();
        foreach (var entry in entries)
        {
            var button = BuildEntryRow(entry);
            EntriesPanel.Children.Add(button);
        }
    }

    public void AddEntry(string label, string description, string iconKey, Func<Task> action)
    {
        var button = BuildEntryRow(new NavigationEntry(label, description, iconKey, action));
        EntriesPanel.Children.Add(button);
    }

    private Button BuildEntryRow(NavigationEntry entry)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        grid.Children.Add(new HavenIcon
        {
            IconKey = entry.IconKey,
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center
        });
        var text = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock { Text = entry.Label, FontWeight = FontWeight.ExtraBold, FontSize = 14 },
                new TextBlock { Text = entry.Description, Classes = { "muted" }, FontSize = 10, TextWrapping = TextWrapping.Wrap }
            }
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 10),
            MinHeight = 48,
            CornerRadius = new CornerRadius(14),
            Content = grid
        };
        button.Classes.Add("sidebar");
        button.Click += async (_, _) => await entry.Action();
        return button;
    }

    public sealed record NavigationEntry(string Label, string Description, string IconKey, Func<Task> Action);
}
