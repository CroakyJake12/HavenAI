using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Core;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Controls;

public static class NotesBlockEditorFactory
{
    public static Control Build(
        NotesWorkspaceViewModel viewModel,
        NotesBlock block,
        Action<NotesBlock> beginEdit,
        Action<NotesBlock, string> endEdit,
        Action refresh,
        Func<Task> importMedia)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(block);
        var body = block.Kind switch
        {
            NotesBlockKind.Paragraph or NotesBlockKind.Heading or NotesBlockKind.Quote or NotesBlockKind.Code =>
                BuildRichText(block, beginEdit, endEdit, refresh),
            NotesBlockKind.List => BuildList(block, beginEdit, endEdit, refresh),
            NotesBlockKind.Table => BuildTable(viewModel, block, beginEdit, endEdit, refresh),
            NotesBlockKind.Image or NotesBlockKind.Audio or NotesBlockKind.Video =>
                BuildMedia(block, beginEdit, endEdit, importMedia),
            NotesBlockKind.Equation => BuildEquation(viewModel, block),
            NotesBlockKind.HtmlWidget => BuildHtml(viewModel, block),
            NotesBlockKind.Canvas => BuildCanvas(viewModel, block, refresh),
            NotesBlockKind.Flashcard => BuildFlashcard(block, beginEdit, endEdit, refresh),
            NotesBlockKind.Divider => new Border
            {
                Height = 2,
                Margin = new Thickness(8, 22),
                Background = ResourceBrush("HavenLineStrongBrush", Color.FromArgb(90, 255, 255, 255))
            },
            _ => new TextBlock { Text = "Unsupported Notes block type: " + block.Kind, Classes = { "muted" } }
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), ColumnSpacing = 6 };
        header.Children.Add(new Border
        {
            Padding = new Thickness(7, 3),
            CornerRadius = new CornerRadius(8),
            Background = ResourceBrush("HavenAccentSoftBrush", Color.FromArgb(54, 47, 128, 237)),
            Child = new TextBlock { Text = block.Kind.ToString(), FontWeight = FontWeight.SemiBold, FontSize = 10 }
        });
        header.Children.Add(WithColumn(new TextBlock
        {
            Text = ReferenceEquals(viewModel.SelectedBlock, block) ? "Selected" : string.Empty,
            Classes = { "muted2" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0)
        }, 1));
        header.Children.Add(WithColumn(SmallButton("↑", () => { viewModel.MoveBlockUpCommand.Execute(block); refresh(); }, "Move block up"), 2));
        header.Children.Add(WithColumn(SmallButton("↓", () => { viewModel.MoveBlockDownCommand.Execute(block); refresh(); }, "Move block down"), 3));
        header.Children.Add(WithColumn(SmallButton("Delete", () => { viewModel.DeleteBlockCommand.Execute(block); refresh(); }, "Delete block", true), 4));

        var card = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(13),
            Background = ResourceBrush("HavenPanelBrush", Color.FromRgb(30, 30, 34)),
            BorderBrush = ReferenceEquals(viewModel.SelectedBlock, block)
                ? ResourceBrush("HavenAccentBrush", Color.FromRgb(47, 128, 237))
                : ResourceBrush("HavenLineBrush", Color.FromArgb(50, 255, 255, 255)),
            BorderThickness = new Thickness(ReferenceEquals(viewModel.SelectedBlock, block) ? 2 : 1),
            Child = new StackPanel { Spacing = 9, Children = { header, body } }
        };
        card.PointerPressed += (_, _) => viewModel.SelectedBlock = block;
        AutomationProperties.SetName(card, block.Kind + " Notes block");
        return card;
    }

    private static Control BuildRichText(
        NotesBlock block,
        Action<NotesBlock> beginEdit,
        Action<NotesBlock, string> endEdit,
        Action refresh)
    {
        if (block.Runs.Count == 0)
        {
            block.Runs.Add(new NotesTextRun
            {
                Text = block.PlainText,
                FontFamily = block.Kind == NotesBlockKind.Code ? "Cascadia Mono" : "Inter",
                FontSize = block.Kind == NotesBlockKind.Heading ? 24 : 14,
                Bold = block.Kind == NotesBlockKind.Heading,
                Italic = block.Kind == NotesBlockKind.Quote
            });
        }

        var root = new StackPanel { Spacing = 8 };
        var style = new ComboBox
        {
            ItemsSource = new[] { "normal", "heading-1", "heading-2", "quote", "code" },
            SelectedItem = block.StyleId,
            MinWidth = 145
        };
        style.SelectionChanged += (_, _) =>
        {
            if (style.SelectedItem is not string selected || selected == block.StyleId) return;
            beginEdit(block);
            block.StyleId = selected;
            block.Kind = selected switch
            {
                "heading-1" or "heading-2" => NotesBlockKind.Heading,
                "quote" => NotesBlockKind.Quote,
                "code" => NotesBlockKind.Code,
                _ => NotesBlockKind.Paragraph
            };
            endEdit(block, "Applied style " + selected);
            refresh();
        };
        var alignment = new ComboBox
        {
            ItemsSource = Enum.GetValues<NotesTextAlignment>(),
            SelectedItem = block.Paragraph.Alignment,
            MinWidth = 120
        };
        alignment.SelectionChanged += (_, _) =>
        {
            if (alignment.SelectedItem is not NotesTextAlignment value || value == block.Paragraph.Alignment) return;
            beginEdit(block);
            block.Paragraph.Alignment = value;
            endEdit(block, "Changed paragraph alignment");
        };
        var lineSpacing = new NumericUpDown
        {
            Minimum = 0.5m,
            Maximum = 10m,
            Increment = 0.25m,
            Value = (decimal)block.Paragraph.LineSpacing,
            Width = 90
        };
        lineSpacing.GotFocus += (_, _) => beginEdit(block);
        lineSpacing.ValueChanged += (_, _) => block.Paragraph.LineSpacing = (double)(lineSpacing.Value ?? 1.25m);
        lineSpacing.LostFocus += (_, _) => endEdit(block, "Changed line spacing");
        root.Children.Add(new WrapPanel { Children = { Labeled("Style", style), Labeled("Alignment", alignment), Labeled("Line spacing", lineSpacing) } });

        for (var index = 0; index < block.Runs.Count; index++)
            root.Children.Add(BuildRunEditor(block, block.Runs[index], index, beginEdit, endEdit, refresh));
        root.Children.Add(SmallButton("+ Formatted run", () =>
        {
            beginEdit(block);
            block.Runs.Add(new NotesTextRun { FontFamily = block.Kind == NotesBlockKind.Code ? "Cascadia Mono" : "Inter" });
            SyncPlainText(block);
            endEdit(block, "Added formatted text run");
            refresh();
        }, "Add independently formatted text"));
        return root;
    }

    private static Control BuildRunEditor(
        NotesBlock block,
        NotesTextRun run,
        int index,
        Action<NotesBlock> beginEdit,
        Action<NotesBlock, string> endEdit,
        Action refresh)
    {
        var text = new TextBox
        {
            Text = run.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = block.Kind == NotesBlockKind.Heading ? 48 : 76,
            FontFamily = new FontFamily(run.FontFamily),
            FontSize = run.FontSize,
            FontWeight = run.Bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = run.Italic ? FontStyle.Italic : FontStyle.Normal
        };
        text.GotFocus += (_, _) => beginEdit(block);
        text.TextChanged += (_, _) => { run.Text = text.Text ?? string.Empty; SyncPlainText(block); };
        text.LostFocus += (_, _) => endEdit(block, "Edited rich text");

        var bold = FormatToggle("B", run.Bold, value => run.Bold = value, beginEdit, endEdit, block, FontWeight.Bold);
        var italic = FormatToggle("I", run.Italic, value => run.Italic = value, beginEdit, endEdit, block, fontStyle: FontStyle.Italic);
        var underline = FormatToggle("U", run.Underline, value => run.Underline = value, beginEdit, endEdit, block);
        var strike = FormatToggle("S", run.StrikeThrough, value => run.StrikeThrough = value, beginEdit, endEdit, block);
        var font = new TextBox { Text = run.FontFamily, MinWidth = 110, Watermark = "Font" };
        var size = new NumericUpDown { Minimum = 4, Maximum = 300, Value = (decimal)run.FontSize, Width = 76 };
        var foreground = new TextBox { Text = run.Foreground, MinWidth = 105, Watermark = "#AARRGGBB" };
        var background = new TextBox { Text = run.Background, MinWidth = 105, Watermark = "#AARRGGBB" };
        var link = new TextBox { Text = run.Link ?? string.Empty, MinWidth = 150, Watermark = "Optional link" };
        foreach (var input in new[] { font, foreground, background, link }) input.GotFocus += (_, _) => beginEdit(block);
        font.LostFocus += (_, _) => { run.FontFamily = string.IsNullOrWhiteSpace(font.Text) ? "Inter" : font.Text.Trim(); endEdit(block, "Changed font"); };
        foreground.LostFocus += (_, _) => { run.Foreground = foreground.Text?.Trim() ?? run.Foreground; endEdit(block, "Changed text colour"); };
        background.LostFocus += (_, _) => { run.Background = background.Text?.Trim() ?? run.Background; endEdit(block, "Changed highlight"); };
        link.LostFocus += (_, _) => { run.Link = string.IsNullOrWhiteSpace(link.Text) ? null : link.Text.Trim(); endEdit(block, "Changed link"); };
        size.GotFocus += (_, _) => beginEdit(block);
        size.ValueChanged += (_, _) => run.FontSize = (double)(size.Value ?? 14m);
        size.LostFocus += (_, _) => endEdit(block, "Changed font size");
        var remove = SmallButton("×", () =>
        {
            if (block.Runs.Count <= 1) return;
            beginEdit(block);
            block.Runs.Remove(run);
            SyncPlainText(block);
            endEdit(block, "Removed formatted text run");
            refresh();
        }, "Remove formatted run", true);

        return new Border
        {
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(9),
            Background = ResourceBrush("HavenPanel2Brush", Color.FromRgb(37, 37, 41)),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "Run " + (index + 1), Classes = { "muted2" }, FontSize = 9 },
                    new WrapPanel { Children = { bold, italic, underline, strike, font, size, foreground, background, link, remove } },
                    text
                }
            }
        };
    }

    private static ToggleButton FormatToggle(
        string label,
        bool value,
        Action<bool> apply,
        Action<NotesBlock> beginEdit,
        Action<NotesBlock, string> endEdit,
        NotesBlock block,
        FontWeight? fontWeight = null,
        FontStyle? fontStyle = null)
    {
        var toggle = new ToggleButton
        {
            Content = label,
            IsChecked = value,
            Width = 34,
            FontWeight = fontWeight ?? FontWeight.Normal,
            FontStyle = fontStyle ?? FontStyle.Normal
        };
        toggle.IsCheckedChanged += (_, _) =>
        {
            beginEdit(block);
            apply(toggle.IsChecked == true);
            endEdit(block, "Changed character formatting");
        };
        return toggle;
    }

    private static Control BuildList(
        NotesBlock block,
        Action<NotesBlock> beginEdit,
        Action<NotesBlock, string> endEdit,
        Action refresh)
    {
        block.List ??= new NotesListData { Items = [new NotesListItem { Text = "List item" }] };
        var root = new StackPanel { Spacing = 7 };
        var type = new ComboBox { ItemsSource = Enum.GetValues<NotesListKind>(), SelectedItem = block.List.Kind, Width = 160 };
        type.SelectionChanged += (_, _) =>
        {
            if (type.SelectedItem is not NotesListKind kind || kind == block.List.Kind) return;
            beginEdit(block);
            block.List.Kind = kind;
            endEdit(block, "Changed list type");
            refresh();
        };
        root.Children.Add(type);
        foreach (var item in block.List.Items.ToArray())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 6 };
            var check = new CheckBox { IsChecked = item.Checked, IsVisible = block.List.Kind == NotesListKind.Checklist };
            check.IsCheckedChanged += (_, _) => { beginEdit(block); item.Checked = check.IsChecked == true; endEdit(block, "Changed checklist item"); };
            var text = new TextBox { Text = item.Text };
            text.GotFocus += (_, _) => beginEdit(block);
            text.TextChanged += (_, _) => item.Text = text.Text ?? string.Empty;
            text.LostFocus += (_, _) => endEdit(block, "Edited list item");
            var level = new NumericUpDown { Minimum = 0, Maximum = 8, Value = item.Level, Width = 60 };
            level.GotFocus += (_, _) => beginEdit(block);
            level.ValueChanged += (_, _) => item.Level = (int)(level.Value ?? 0m);
            level.LostFocus += (_, _) => endEdit(block, "Changed list nesting");
            var remove = SmallButton("×", () =>
            {
                if (block.List.Items.Count <= 1) return;
                beginEdit(block);
                block.List.Items.Remove(item);
                endEdit(block, "Removed list item");
                refresh();
            }, "Remove list item", true);
            row.Children.Add(check);
            row.Children.Add(WithColumn(text, 1));
            row.Children.Add(WithColumn(level, 2));
            row.Children.Add(WithColumn(remove, 3));
            root.Children.Add(row);
        }
        root.Children.Add(SmallButton("+ Item", () =>
        {
            beginEdit(block);
            block.List.Items.Add(new NotesListItem { Text = "New item" });
            endEdit(block, "Added list item");
            refresh();
        }, "Add list item"));
        return root;
    }

    private static Control BuildTable(
        NotesWorkspaceViewModel viewModel,
        NotesBlock block,
        Action<NotesBlock> beginEdit,
        Action<NotesBlock, string> endEdit,
        Action refresh)
    {
        block.Table ??= NotesTableData.Create(3, 3);
        var root = new StackPanel { Spacing = 7 };
        root.Children.Add(new WrapPanel
        {
            Children =
            {
                SmallButton("+ Row", () => { viewModel.AddTableRow(block); refresh(); }, "Add table row"),
                SmallButton("− Row", () => { viewModel.RemoveTableRow(block); refresh(); }, "Remove last table row"),
                SmallButton("+ Column", () => { viewModel.AddTableColumn(block); refresh(); }, "Add table column"),
                SmallButton("− Column", () => { viewModel.RemoveTableColumn(block); refresh(); }, "Remove last table column")
            }
        });
        var tableGrid = new Grid { RowSpacing = 3, ColumnSpacing = 3 };
        var rows = block.Table.Rows.Count;
        var columns = block.Table.Rows.Max(row => row.Cells.Count);
        for (var row = 0; row < rows; row++) tableGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var column = 0; column < columns; column++) tableGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var rowIndex = 0; rowIndex < rows; rowIndex++)
        for (var columnIndex = 0; columnIndex < columns; columnIndex++)
        {
            var cell = block.Table.Rows[rowIndex].Cells[columnIndex];
            var editor = new TextBox
            {
                Text = cell.Text,
                MinWidth = 80,
                MinHeight = 36,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = rowIndex == 0 && block.Table.HeaderRow ? FontWeight.SemiBold : FontWeight.Normal
            };
            editor.GotFocus += (_, _) => beginEdit(block);
            editor.TextChanged += (_, _) => cell.Text = editor.Text ?? string.Empty;
            editor.LostFocus += (_, _) => endEdit(block, "Edited table cell");
            Grid.SetRow(editor, rowIndex);
            Grid.SetColumn(editor, columnIndex);
            tableGrid.Children.Add(editor);
        }
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = tableGrid
        });
        return root;
    }

    private static Control BuildMedia(
        NotesBlock block,
        Action<NotesBlock> beginEdit,
        Action<NotesBlock, string> endEdit,
        Func<Task> importMedia)
    {
        if (block.Media is null) return new TextBlock { Text = "Media metadata is missing.", Classes = { "muted" } };
        var media = block.Media;
        var alt = new TextBox { Text = media.AltText, Watermark = "Accessible alternative text" };
        var caption = new TextBox { Text = media.Caption, Watermark = "Caption", AcceptsReturn = true };
        var wrapping = new ComboBox
        {
            ItemsSource = new[] { "Inline", "Square", "Tight", "Behind text", "In front of text" },
            SelectedItem = media.Wrapping
        };
        var width = new NumericUpDown { Minimum = 1, Maximum = 10000, Value = (decimal)media.Width };
        var height = new NumericUpDown { Minimum = 1, Maximum = 10000, Value = (decimal)media.Height };
        foreach (var input in new[] { alt, caption }) input.GotFocus += (_, _) => beginEdit(block);
        alt.LostFocus += (_, _) => { media.AltText = alt.Text ?? string.Empty; endEdit(block, "Edited media alternative text"); };
        caption.LostFocus += (_, _) => { media.Caption = caption.Text ?? string.Empty; endEdit(block, "Edited media caption"); };
        wrapping.SelectionChanged += (_, _) => { beginEdit(block); media.Wrapping = wrapping.SelectedItem as string ?? "Inline"; endEdit(block, "Changed media wrapping"); };
        width.GotFocus += (_, _) => beginEdit(block);
        width.ValueChanged += (_, _) => media.Width = (double)(width.Value ?? 400m);
        width.LostFocus += (_, _) => endEdit(block, "Changed media size");
        height.GotFocus += (_, _) => beginEdit(block);
        height.ValueChanged += (_, _) => media.Height = (double)(height.Value ?? 300m);
        height.LostFocus += (_, _) => endEdit(block, "Changed media size");
        var dimensions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 7 };
        dimensions.Children.Add(Labeled("Width", width));
        dimensions.Children.Add(WithColumn(Labeled("Height", height), 1));
        return new StackPanel
        {
            Spacing = 7,
            Children =
            {
                new TextBlock { Text = media.OriginalName + $" · {media.MediaType} · {media.SizeBytes / 1024d:0.0} KB", FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "SHA-256 " + media.Sha256, Classes = { "muted2" }, FontSize = 9, TextTrimming = TextTrimming.CharacterEllipsis },
                Labeled("Alternative text", alt),
                Labeled("Caption", caption),
                Labeled("Wrapping", wrapping),
                dimensions,
                SmallButton("Insert another media block", () => _ = importMedia(), "Import media")
            }
        };
    }

    private static Control BuildEquation(NotesWorkspaceViewModel viewModel, NotesBlock block)
    {
        block.Equation ??= new NotesEquationData();
        var equation = block.Equation;
        var mode = new ComboBox { ItemsSource = Enum.GetValues<NotesEquationViewMode>(), SelectedItem = equation.ViewMode, Width = 120 };
        var source = new TextBox
        {
            Text = equation.Source,
            AcceptsReturn = true,
            MinHeight = 100,
            FontFamily = new FontFamily("Cascadia Mono"),
            TextWrapping = TextWrapping.Wrap
        };
        var alternative = new TextBox
        {
            Text = equation.AccessibleAlternative,
            Watermark = "Accessible spoken or textual alternative",
            AcceptsReturn = true,
            MinHeight = 55
        };
        var rendered = new TextBlock { Text = equation.RenderedText, FontSize = 24, TextWrapping = TextWrapping.Wrap };
        var error = new TextBlock { Text = equation.Error, Foreground = ResourceBrush("HavenDangerBrush", Colors.IndianRed), TextWrapping = TextWrapping.Wrap };
        var numbered = new CheckBox { Content = "Number equation", IsChecked = equation.Numbered };
        var label = new TextBox { Text = equation.Label, Watermark = "Equation label" };
        var ready = false;
        void Apply()
        {
            if (!ready) return;
            equation.Numbered = numbered.IsChecked == true;
            if (equation.Numbered && equation.Number is null) equation.Number = 1;
            equation.Label = label.Text ?? string.Empty;
            viewModel.UpdateEquation(
                block,
                source.Text ?? string.Empty,
                mode.SelectedItem is NotesEquationViewMode selected ? selected : NotesEquationViewMode.Split,
                alternative.Text ?? string.Empty);
            rendered.Text = equation.RenderedText;
            error.Text = equation.Error;
        }
        source.LostFocus += (_, _) => Apply();
        alternative.LostFocus += (_, _) => Apply();
        mode.SelectionChanged += (_, _) => Apply();
        numbered.IsCheckedChanged += (_, _) => Apply();
        label.LostFocus += (_, _) => Apply();
        ready = true;
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 9 };
        split.Children.Add(new StackPanel { Spacing = 5, Children = { new TextBlock { Text = "LaTeX source", Classes = { "eyebrow" } }, source, error } });
        split.Children.Add(WithColumn(new StackPanel { Spacing = 5, Children = { new TextBlock { Text = "Visual result", Classes = { "eyebrow" } }, rendered, alternative } }, 1));
        var options = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 7 };
        options.Children.Add(mode);
        options.Children.Add(WithColumn(numbered, 1));
        options.Children.Add(WithColumn(label, 2));
        return new StackPanel { Spacing = 7, Children = { options, split } };
    }

    private static Control BuildHtml(NotesWorkspaceViewModel viewModel, NotesBlock block)
    {
        block.Html ??= new NotesHtmlData();
        var data = block.Html;
        var mode = new ComboBox { ItemsSource = Enum.GetValues<NotesHtmlViewMode>(), SelectedItem = data.ViewMode, Width = 120 };
        var scripts = new CheckBox { Content = "Allow scripts", IsChecked = data.AllowScripts };
        var network = new CheckBox { Content = "Allow HTTPS network requests", IsChecked = data.AllowNetwork };
        var forms = new CheckBox { Content = "Allow forms", IsChecked = data.AllowForms };
        var html = SourceEditor("HTML", data.HtmlSource);
        var css = SourceEditor("CSS", data.CssSource);
        var javascript = SourceEditor("JavaScript", data.JavaScriptSource);
        var preview = new NotesHtmlPreviewControl();
        preview.UpdatePreview(data);
        var error = new TextBlock { Text = data.LastSecurityError, Foreground = ResourceBrush("HavenDangerBrush", Colors.IndianRed), TextWrapping = TextWrapping.Wrap };
        var ready = false;
        void Apply()
        {
            if (!ready) return;
            viewModel.UpdateHtml(
                block,
                html.Text ?? string.Empty,
                css.Text ?? string.Empty,
                javascript.Text ?? string.Empty,
                scripts.IsChecked == true,
                network.IsChecked == true,
                forms.IsChecked == true,
                mode.SelectedItem is NotesHtmlViewMode selected ? selected : NotesHtmlViewMode.Split);
            error.Text = data.LastSecurityError;
            preview.UpdatePreview(data);
        }
        foreach (var source in new[] { html, css, javascript }) source.LostFocus += (_, _) => Apply();
        mode.SelectionChanged += (_, _) => Apply();
        scripts.IsCheckedChanged += (_, _) => Apply();
        network.IsCheckedChanged += (_, _) => Apply();
        forms.IsCheckedChanged += (_, _) => Apply();
        ready = true;
        var sources = new TabControl
        {
            ItemsSource = new object[]
            {
                new TabItem { Header = "HTML", Content = html },
                new TabItem { Header = "CSS", Content = css },
                new TabItem { Header = "JavaScript", Content = javascript }
            }
        };
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 9 };
        split.Children.Add(sources);
        split.Children.Add(WithColumn(preview, 1));
        return new StackPanel
        {
            Spacing = 7,
            Children =
            {
                new WrapPanel { Children = { mode, scripts, network, forms } },
                new TextBlock
                {
                    Text = "Popups, object embeds, external frames and top-level navigation are always blocked. Source and permission changes must agree before preview runs.",
                    Classes = { "muted2" },
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 9
                },
                error,
                split
            }
        };
    }

    private static Control BuildCanvas(NotesWorkspaceViewModel viewModel, NotesBlock block, Action refresh)
    {
        block.Canvas ??= new NotesCanvasData();
        var canvas = block.Canvas;
        var ink = new NotesInkCanvasControl { CanvasData = canvas };
        var tool = new ComboBox { ItemsSource = new[] { "pen", "highlighter", "eraser" }, SelectedItem = "pen", Width = 120 };
        var colour = new TextBox { Text = "#FF2F80ED", Width = 120 };
        var width = new Slider { Minimum = 0.5, Maximum = 40, Value = 2.5, Width = 150 };
        var infinite = new CheckBox { Content = "Infinite canvas", IsChecked = canvas.Infinite };
        var layers = new ComboBox
        {
            ItemsSource = canvas.GhostLayers,
            Width = 170,
            ItemTemplate = new FuncDataTemplate<NotesGhostLayer>((layer, _) =>
                new TextBlock { Text = layer?.Name ?? "Ghost layer" })
        };
        void SyncTool()
        {
            ink.Tool = tool.SelectedItem as string ?? "pen";
            ink.Colour = colour.Text?.Trim() ?? "#FF2F80ED";
            ink.StrokeWidth = width.Value;
            ink.ActiveGhostLayerId = (layers.SelectedItem as NotesGhostLayer)?.Id;
        }
        tool.SelectionChanged += (_, _) => SyncTool();
        colour.LostFocus += (_, _) => SyncTool();
        width.ValueChanged += (_, _) => SyncTool();
        infinite.IsCheckedChanged += (_, _) =>
        {
            viewModel.BeginBlockEdit(block);
            canvas.Infinite = infinite.IsChecked == true;
            viewModel.CommitBlockEdit(block, "Changed canvas extent");
            ink.InvalidateVisual();
        };
        layers.SelectionChanged += (_, _) => SyncTool();
        ink.StrokeCompleted += (_, stroke) =>
        {
            viewModel.AddInkStroke(block, stroke);
            if (stroke.GhostLayerId is { } layerId
                && canvas.GhostLayers.FirstOrDefault(item => item.Id == layerId) is { } layer)
                viewModel.AssignStrokeToGhostLayer(block, stroke, layer);
            RefreshLayerItems();
            ink.InvalidateVisual();
        };
        ink.StrokeErased += (_, id) => { viewModel.RemoveInkStroke(block, id); ink.InvalidateVisual(); };

        var addLayer = SmallButton("+ Ghost layer", () =>
        {
            var layer = viewModel.AddGhostLayer(block, "Answer " + (canvas.GhostLayers.Count + 1), NotesGhostRevealMode.Tap);
            RefreshLayerItems();
            layers.SelectedItem = layer;
            SyncTool();
            refresh();
        }, "Add revealable Ghost Pen layer");
        var reveal = SmallButton("Reveal / hide", () =>
        {
            if (layers.SelectedItem is not NotesGhostLayer layer) return;
            viewModel.ToggleGhostLayer(block, layer);
            ink.InvalidateVisual();
            refresh();
        }, "Reveal or hide selected Ghost layer");
        var addObject = SmallButton("+ Text object", () =>
        {
            viewModel.BeginBlockEdit(block);
            canvas.Objects.Add(new NotesCanvasObject
            {
                Kind = NotesCanvasObjectKind.Text,
                Text = "Canvas note",
                X = 80 + canvas.Objects.Count * 20,
                Y = 80 + canvas.Objects.Count * 20
            });
            viewModel.CommitBlockEdit(block, "Added canvas object");
            ink.InvalidateVisual();
            refresh();
        }, "Add editable canvas text object");
        var frame = SmallButton("+ Frame", () =>
        {
            viewModel.BeginBlockEdit(block);
            canvas.Objects.Add(new NotesCanvasObject
            {
                Kind = NotesCanvasObjectKind.Frame,
                Text = "Frame",
                X = 40,
                Y = 40,
                Width = 500,
                Height = 320,
                ZIndex = -1
            });
            viewModel.CommitBlockEdit(block, "Added canvas frame");
            ink.InvalidateVisual();
            refresh();
        }, "Add canvas frame");
        return new StackPanel
        {
            Spacing = 7,
            Children =
            {
                new WrapPanel { Children = { tool, colour, width, infinite, layers, addLayer, reveal, addObject, frame } },
                new TextBlock
                {
                    Text = "Pen pressure and tilt are stored per point. Mouse and touch use a safe fallback pressure. Use the wheel to zoom.",
                    Classes = { "muted2" },
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 9
                },
                new Border
                {
                    CornerRadius = new CornerRadius(9),
                    BorderBrush = ResourceBrush("HavenLineStrongBrush", Color.FromArgb(80, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Child = ink
                },
                BuildCanvasObjectList(viewModel, block, ink, refresh)
            }
        };

        void RefreshLayerItems()
        {
            layers.ItemsSource = null;
            layers.ItemsSource = canvas.GhostLayers;
        }
    }

    private static Control BuildCanvasObjectList(
        NotesWorkspaceViewModel viewModel,
        NotesBlock block,
        NotesInkCanvasControl ink,
        Action refresh)
    {
        var panel = new StackPanel { Spacing = 4 };
        foreach (var canvasObject in block.Canvas!.Objects.OrderBy(item => item.ZIndex).ToArray())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 6 };
            row.Children.Add(new TextBlock
            {
                Text = canvasObject.Kind.ToString(),
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "muted2" },
                FontSize = 9
            });
            var text = new TextBox { Text = canvasObject.Text };
            text.GotFocus += (_, _) => viewModel.BeginBlockEdit(block);
            text.TextChanged += (_, _) => canvasObject.Text = text.Text ?? string.Empty;
            text.LostFocus += (_, _) =>
            {
                viewModel.CommitBlockEdit(block, "Edited canvas object");
                ink.InvalidateVisual();
            };
            var locked = new CheckBox { Content = "Lock", IsChecked = canvasObject.Locked };
            locked.IsCheckedChanged += (_, _) =>
            {
                viewModel.BeginBlockEdit(block);
                canvasObject.Locked = locked.IsChecked == true;
                viewModel.CommitBlockEdit(block, "Changed canvas object lock");
                ink.InvalidateVisual();
            };
            var remove = SmallButton("×", () =>
            {
                viewModel.BeginBlockEdit(block);
                block.Canvas.Objects.Remove(canvasObject);
                viewModel.CommitBlockEdit(block, "Removed canvas object");
                ink.InvalidateVisual();
                refresh();
            }, "Remove canvas object", true);
            row.Children.Add(WithColumn(text, 1));
            row.Children.Add(WithColumn(locked, 2));
            row.Children.Add(WithColumn(remove, 3));
            panel.Children.Add(row);
        }
        return panel;
    }

    private static Control BuildFlashcard(
        NotesBlock block,
        Action<NotesBlock> beginEdit,
        Action<NotesBlock, string> endEdit,
        Action refresh)
    {
        block.Flashcard ??= new NotesFlashcardData { Front = "Question", Back = "Answer" };
        var card = block.Flashcard;
        var front = new TextBox { Text = card.Front, AcceptsReturn = true, MinHeight = 70, TextWrapping = TextWrapping.Wrap };
        var back = new TextBox { Text = card.Back, AcceptsReturn = true, MinHeight = 90, TextWrapping = TextWrapping.Wrap };
        var hint = new TextBox { Text = card.Hint, Watermark = "Optional hint" };
        foreach (var input in new[] { front, back, hint }) input.GotFocus += (_, _) => beginEdit(block);
        front.TextChanged += (_, _) => card.Front = front.Text ?? string.Empty;
        back.TextChanged += (_, _) => card.Back = back.Text ?? string.Empty;
        hint.TextChanged += (_, _) => card.Hint = hint.Text ?? string.Empty;
        front.LostFocus += (_, _) => endEdit(block, "Edited flashcard question");
        back.LostFocus += (_, _) => endEdit(block, "Edited flashcard answer");
        hint.LostFocus += (_, _) => endEdit(block, "Edited flashcard hint");
        var masks = new StackPanel { Spacing = 4 };
        foreach (var mask in card.OcclusionMasks)
        {
            masks.Children.Add(new TextBlock
            {
                Text = $"Occlusion {mask.X:0},{mask.Y:0} · {mask.Width:0}×{mask.Height:0} · {mask.Answer}",
                Classes = { "muted" },
                FontSize = 9
            });
        }
        var addMask = SmallButton("+ Image occlusion", () =>
        {
            beginEdit(block);
            card.OcclusionMasks.Add(new NotesOcclusionMask
            {
                X = 20 + card.OcclusionMasks.Count * 10,
                Y = 20 + card.OcclusionMasks.Count * 10,
                Width = 140,
                Height = 70,
                Answer = "Hidden answer"
            });
            endEdit(block, "Added flashcard image occlusion");
            refresh();
        }, "Add editable image or diagram occlusion mask");
        return new StackPanel
        {
            Spacing = 7,
            Children =
            {
                Labeled("Front", front),
                Labeled("Back", back),
                Labeled("Hint", hint),
                new TextBlock
                {
                    Text = $"Due {card.Schedule.DueAt.LocalDateTime:g} · interval {card.Schedule.IntervalDays} days · repetitions {card.Schedule.Repetitions} · lapses {card.Schedule.Lapses}",
                    Classes = { "muted2" },
                    FontSize = 9
                },
                addMask,
                masks
            }
        };
    }

    private static TextBox SourceEditor(string name, string value) => new()
    {
        Text = value,
        AcceptsReturn = true,
        MinHeight = 210,
        FontFamily = new FontFamily("Cascadia Mono"),
        FontSize = 12,
        TextWrapping = TextWrapping.NoWrap,
        Watermark = name + " source"
    };

    private static void SyncPlainText(NotesBlock block) =>
        block.PlainText = string.Concat(block.Runs.Select(run => run.Text));

    private static Button SmallButton(string label, Action action, string tooltip, bool danger = false)
    {
        var button = new Button { Content = label, Margin = new Thickness(2) };
        button.Classes.Add(danger ? "danger" : "secondary");
        button.Click += (_, _) => action();
        ToolTip.SetTip(button, tooltip);
        AutomationProperties.SetName(button, tooltip);
        return button;
    }

    private static Control Labeled(string label, Control control) => new StackPanel
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label, Classes = { "muted" }, FontSize = 9 },
            control
        }
    };

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

    private static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
