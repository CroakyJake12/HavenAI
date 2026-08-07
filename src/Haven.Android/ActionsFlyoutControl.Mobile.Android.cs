using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Haven.Desktop.Views.Shell.TopRail;

public sealed partial class ActionsFlyoutControl
{
    private bool _androidMobileLayoutWired;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (!_androidMobileLayoutWired)
        {
            _androidMobileLayoutWired = true;
            SearchBox.TextChanged += (_, _) => ApplyAndroidMobileLayout();
        }

        ApplyAndroidMobileLayout();
    }

    private void ApplyAndroidMobileLayout()
    {
        var topWidth = TopLevel.GetTopLevel(this)?.Bounds.Width ?? 380;
        var width = Math.Clamp(topWidth - 20, 280, 390);

        Width = width;
        MinWidth = 0;
        MaxWidth = width;

        if (Content is StackPanel root)
        {
            foreach (var border in root.Children.OfType<Border>())
            {
                border.Width = width;
                border.MinWidth = 0;
                border.MaxWidth = width;
            }
        }

        SearchBox.MinWidth = 0;
        SearchBox.MaxWidth = Math.Max(220, width - 40);

        foreach (var grid in SectionsPanel.Children.OfType<Grid>())
        {
            var buttons = grid.Children.OfType<Button>().ToArray();
            if (buttons.Length == 0)
                continue;

            grid.ColumnDefinitions = new ColumnDefinitions("*");
            grid.RowDefinitions = new RowDefinitions(
                string.Join(',', Enumerable.Repeat("Auto", buttons.Length)));
            grid.ColumnSpacing = 0;
            grid.RowSpacing = 8;

            for (var index = 0; index < buttons.Length; index++)
            {
                Grid.SetColumn(buttons[index], 0);
                Grid.SetRow(buttons[index], index);
            }
        }
    }
}
