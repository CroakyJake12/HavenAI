using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Boards;

public sealed partial class BoardsPage
{
    private void BuildLiveList(StackPanel host, BoardsLiveComponent component)
    {
        foreach (var item in component.Items)
        {
            var local = item;
            var input = new TextBox { Text = local.Text };
            AutomationProperties.SetName(input, "Shared list item");
            input.LostFocus += (_, _) =>
            {
                var next = input.Text ?? string.Empty;
                if (next == local.Text) return;
                _boards.UpdateComponentItem(_document!, component.Id, local.Id, value => value.Text = next);
                SetStatus("Shared list synchronized to every placement");
                RebuildEditor();
            };
            host.Children.Add(input);
        }
    }

    private void BuildLiveTable(StackPanel host, BoardsLiveComponent component)
    {
        var columns = Math.Max(1, component.Items.Select(item => item.Cells.Count).DefaultIfEmpty(1).Max());
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", columns))),
            RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", Math.Max(1, component.Items.Count)))),
            ColumnSpacing = 4,
            RowSpacing = 4
        };

        for (var r = 0; r < component.Items.Count; r++)
        {
            var item = component.Items[r];
            for (var c = 0; c < item.Cells.Count; c++)
            {
                var localItem = item;
                var cellIndex = c;
                var input = new TextBox { Text = localItem.Cells[cellIndex], MinWidth = 100 };
                AutomationProperties.SetName(input, $"Shared table cell {r + 1}, {c + 1}");
                Grid.SetRow(input, r);
                Grid.SetColumn(input, c);
                input.LostFocus += (_, _) =>
                {
                    var next = input.Text ?? string.Empty;
                    if (next == localItem.Cells[cellIndex]) return;
                    _boards.UpdateComponentItem(_document!, component.Id, localItem.Id, value =>
                    {
                        while (value.Cells.Count <= cellIndex) value.Cells.Add(string.Empty);
                        value.Cells[cellIndex] = next;
                    });
                    SetStatus("Shared table synchronized to every placement");
                    RebuildEditor();
                };
                grid.Children.Add(input);
            }
        }

        host.Children.Add(grid);
    }
}
