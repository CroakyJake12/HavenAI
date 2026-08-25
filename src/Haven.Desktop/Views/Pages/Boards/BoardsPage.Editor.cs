using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Pages.Boards;

public sealed partial class BoardsPage
{
    private bool _suppressEditorRebuild;

    private void RebuildEditor()
    {
        _editor.Children.Clear();
        if (_page is null || _document is null)
        {
            _editor.Children.Add(new TextBlock { Text = "Choose a page to begin." });
            return;
        }

        var title = new TextBox { Text = _page.Title, FontSize = 22 };
        AutomationProperties.SetName(title, "Page title");
        title.TextChanged += (_, _) =>
        {
            var next = string.IsNullOrWhiteSpace(title.Text) ? "Untitled page" : title.Text.Trim();
            if (_page.Title == next) return;
            _boards.RenamePage(_document, _page.Id, next);
            RebuildPageTabs();
            SetStatus("Unsaved page title");
        };
        _editor.Children.Add(title);
        if (_freeformMode)
        {
            _editor.Children.Add(BuildFreeformSurface());
            return;
        }

        foreach (var block in _page.Blocks.OrderBy(item => item.Order))
            _editor.Children.Add(BuildBlock(block));
    }

    private Control BuildBlock(NotesBlock block)
    {
        if (block.Metadata.TryGetValue(BoardsWorkspaceService.ComponentIdKey, out var raw)
            && Guid.TryParse(raw, out var componentId))
        {
            var component = _boards.GetComponents(_document!).FirstOrDefault(item => item.Id == componentId);
            if (component is not null) return BuildLiveComponent(block, component);
        }

        if (block.Media is not null) return BuildAttachmentBlock(block);

        var card = new StackPanel { Spacing = 6, Margin = new Thickness(0, 5, 0, 10) };
        card.Children.Add(new TextBlock { Text = block.Kind.ToString(), FontSize = 10 });

        switch (block.Kind)
        {
            case NotesBlockKind.Paragraph:
            case NotesBlockKind.Heading:
            case NotesBlockKind.Quote:
            case NotesBlockKind.Code:
                card.Children.Add(BuildRichText(block));
                break;
            case NotesBlockKind.List:
                BuildList(card, block);
                break;
            case NotesBlockKind.Table:
                BuildTable(card, block);
                break;
            case NotesBlockKind.Canvas:
                BuildInk(card, block);
                break;
            case NotesBlockKind.HtmlWidget:
                BuildEmbed(card, block);
                break;
            case NotesBlockKind.Image:
            case NotesBlockKind.Audio:
            case NotesBlockKind.Video:
                card.Children.Add(new TextBlock
                {
                    Text = block.Media is null ? "Media block" : $"{block.Media.OriginalName}\n{block.Media.Caption}",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
                break;
            default:
                card.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(block.PlainText) ? $"{block.Kind} content is preserved." : block.PlainText,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
                break;
        }
        return card;
    }

    private Control BuildRichText(NotesBlock block)
    {
        var host = new StackPanel { Spacing = 5 };
        var format = new WrapPanel();
        var text = new TextBox
        {
            Text = block.Runs.Count > 0 ? string.Concat(block.Runs.Select(run => run.Text)) : block.PlainText,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = block.Kind == NotesBlockKind.Heading ? 60 : 90
        };
        AutomationProperties.SetName(text, block.Kind == NotesBlockKind.Heading ? "Heading text" : "Rich text");

        foreach (var pair in new[]
        {
            ("Bold", WriteCharacterFormat.Bold),
            ("Italic", WriteCharacterFormat.Italic),
            ("Underline", WriteCharacterFormat.Underline),
            ("Strike", WriteCharacterFormat.StrikeThrough)
        })
        {
            var button = new Button { Content = pair.Item1 };
            button.Click += (_, _) =>
            {
                if (_documentEditor is null) return;
                _documentEditor.SelectBlock(block.Id, text.CaretIndex);
                _documentEditor.ToggleCharacter(pair.Item2);
                RebuildEditor();
            };
            format.Children.Add(button);
        }

        text.TextChanged += (_, _) =>
        {
            if (_documentEditor is null || _suppressEditorRebuild) return;
            _suppressEditorRebuild = true;
            try
            {
                _documentEditor.SelectBlock(block.Id, text.CaretIndex);
                _documentEditor.ReplaceSelectedText(text.Text ?? string.Empty, text.CaretIndex);
                SetStatus("Unsaved rich-text changes");
            }
            finally
            {
                _suppressEditorRebuild = false;
            }
        };
        host.Children.Add(format);
        host.Children.Add(text);
        return host;
    }

    private void BuildList(StackPanel host, NotesBlock block)
    {
        if (block.List is null) return;
        foreach (var item in block.List.Items)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
            if (block.List.Kind == NotesListKind.Checklist)
            {
                var check = new CheckBox { IsChecked = item.Checked };
                AutomationProperties.SetName(check, "Checklist item complete");
                check.IsCheckedChanged += (_, _) =>
                {
                    _boards.UpdateListItem(_document!, _page!.Id, block.Id, item.Id, isChecked: check.IsChecked == true);
                    SetStatus("Unsaved checklist changes");
                };
                row.Children.Add(check);
            }
            var input = new TextBox { Text = item.Text };
            Grid.SetColumn(input, 1);
            input.TextChanged += (_, _) =>
            {
                _boards.UpdateListItem(_document!, _page!.Id, block.Id, item.Id, text: input.Text ?? string.Empty);
                SetStatus("Unsaved checklist changes");
            };
            row.Children.Add(input);
            host.Children.Add(row);
        }
    }

    private void BuildTable(StackPanel host, NotesBlock block)
    {
        if (block.Table is null || block.Table.Rows.Count == 0) return;
        var columns = Math.Max(1, block.Table.Rows.Max(row => row.Cells.Count));
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", columns))),
            RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", block.Table.Rows.Count))),
            ColumnSpacing = 4,
            RowSpacing = 4
        };

        for (var r = 0; r < block.Table.Rows.Count; r++)
        for (var c = 0; c < block.Table.Rows[r].Cells.Count; c++)
        {
            var cell = block.Table.Rows[r].Cells[c];
            var input = new TextBox { Text = cell.Text, MinWidth = 100 };
            AutomationProperties.SetName(input, $"Table cell {r + 1}, {c + 1}");
            Grid.SetRow(input, r);
            Grid.SetColumn(input, c);
            input.TextChanged += (_, _) =>
            {
                _boards.UpdateTableCell(_document!, _page!.Id, block.Id, cell.Id, input.Text ?? string.Empty);
                SetStatus("Unsaved table changes");
            };
            grid.Children.Add(input);
        }
        host.Children.Add(grid);
    }

    private void BuildInk(StackPanel host, NotesBlock block)
    {
        if (block.Canvas is null) return;
        var toolbar = new WrapPanel();
        var ink = new NotesInkCanvasControl
        {
            CanvasData = block.Canvas,
            MinHeight = 420,
            Tool = "pen"
        };
        AutomationProperties.SetName(ink, "Boards ink canvas");

        var pen = new Button { Content = "Pen" };
        pen.Click += (_, _) => ink.Tool = "pen";
        var eraser = new Button { Content = "Eraser" };
        eraser.Click += (_, _) => ink.Tool = "eraser";
        toolbar.Children.Add(pen);
        toolbar.Children.Add(eraser);

        ink.StrokeCompleted += (_, stroke) =>
        {
            block.Canvas.Strokes.Add(stroke);
            _document!.UpdatedAt = DateTimeOffset.UtcNow;
            ink.InvalidateVisual();
            SetStatus("Unsaved ink");
        };
        ink.StrokeErased += (_, id) =>
        {
            var stroke = block.Canvas.Strokes.FirstOrDefault(item => item.Id == id);
            if (stroke is not null) block.Canvas.Strokes.Remove(stroke);
            _document!.UpdatedAt = DateTimeOffset.UtcNow;
            ink.InvalidateVisual();
            SetStatus("Unsaved ink");
        };
        ink.ViewChanged += (_, _) =>
        {
            _document!.UpdatedAt = DateTimeOffset.UtcNow;
            SetStatus("Unsaved canvas view");
        };

        host.Children.Add(toolbar);
        host.Children.Add(ink);
    }

    private Control BuildLiveComponent(NotesBlock placement, BoardsLiveComponent component)
    {
        var card = new StackPanel { Spacing = 7, Margin = new Thickness(0, 7, 0, 12) };
        card.Children.Add(new TextBlock
        {
            Text = $"{component.Title} · {component.Source.Availability} · v{component.Version}",
            FontSize = 14,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });

        if (!string.IsNullOrWhiteSpace(component.Source.DisplayName) || !string.IsNullOrWhiteSpace(component.Source.Provider))
            card.Children.Add(new TextBlock { Text = $"Source: {component.Source.DisplayName} ({component.Source.Provider})", FontSize = 11 });
        if (component.Source.Availability == BoardsLiveAvailability.Unavailable && !string.IsNullOrWhiteSpace(component.Source.UnavailableReason))
            card.Children.Add(new TextBlock { Text = component.Source.UnavailableReason, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

        switch (component.Kind)
        {
            case BoardsLiveComponentKind.TaskList:
                foreach (var item in component.Items)
                {
                    var local = item;
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
                    var check = new CheckBox { IsChecked = local.Checked };
                    var input = new TextBox { Text = local.Text };
                    check.IsCheckedChanged += (_, _) =>
                    {
                        _boards.UpdateComponentItem(_document!, component.Id, local.Id, value => value.Checked = check.IsChecked == true);
                        SetStatus("Live component updated across placements");
                    };
                    input.TextChanged += (_, _) =>
                    {
                        if (input.Text == local.Text) return;
                        _boards.UpdateComponentItem(_document!, component.Id, local.Id, value => value.Text = input.Text ?? string.Empty);
                        SetStatus("Live component updated across placements");
                    };
                    Grid.SetColumn(input, 1);
                    row.Children.Add(check);
                    row.Children.Add(input);
                    card.Children.Add(row);
                }
                break;

            case BoardsLiveComponentKind.Poll:
                foreach (var item in component.Items)
                {
                    var local = item;
                    var vote = new Button { Content = $"{local.Text} · {local.Votes} votes" };
                    vote.Click += (_, _) =>
                    {
                        _boards.UpdateComponentItem(_document!, component.Id, local.Id, value => value.Votes++);
                        RebuildEditor();
                        SetStatus("Poll vote synchronized to every placement");
                    };
                    card.Children.Add(vote);
                }
                break;

            case BoardsLiveComponentKind.Status:
                foreach (var item in component.Items)
                {
                    var local = item;
                    var row = new WrapPanel();
                    row.Children.Add(new TextBlock { Text = local.Text, VerticalAlignment = VerticalAlignment.Center });
                    foreach (var state in new[] { "Not started", "On track", "At risk", "Done" })
                    {
                        var next = state;
                        var button = new Button { Content = state };
                        button.Click += (_, _) =>
                        {
                            _boards.UpdateComponentItem(_document!, component.Id, local.Id, value => value.Status = next);
                            RebuildEditor();
                            SetStatus("Status synchronized to every placement");
                        };
                        row.Children.Add(button);
                    }
                    card.Children.Add(row);
                }
                break;

            case BoardsLiveComponentKind.Table:
                BuildLiveTable(card, component);
                break;

            case BoardsLiveComponentKind.List:
                BuildLiveList(card, component);
                break;
        }

        var duplicate = new Button { Content = "Place again on this page" };
        duplicate.Click += async (_, _) =>
        {
            _boards.PlaceComponent(_document!, _page!, component.Id);
            RebuildEditor();
            await SaveAsync("Placed existing live component");
        };
        card.Children.Add(duplicate);
        return card;
    }
}
