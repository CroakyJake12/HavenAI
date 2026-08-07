using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Haven.Desktop.Views.Shell.TopRail;

public sealed partial class ActionsFlyoutControl
{
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyAndroidMobileLayout();
    }

    private void ApplyAndroidMobileLayout()
    {
        var topWidth = TopLevel.GetTopLevel(this)?.Bounds.Width ?? 380;
        var width = Math.Clamp(topWidth - 20, 280, 390);

        if (Content is StackPanel root)
        {
            foreach (var border in root.Children.OfType<Border>())
                border.Width = width;
        }

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
