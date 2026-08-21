using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Write;

internal sealed partial class WordWriteHavenScene
{
    private static readonly string[] CommonFonts = ["Montserrat", "Arial", "Calibri", "Cambria", "Georgia", "Times New Roman", "Cascadia Mono"];
    private static readonly string[] CommonSizes = ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "32", "36", "48", "72"];

    private void OnDocumentSelectionChanged(object? sender, EventArgs e)
    {
        if (_editor is null) return;
        RebuildRibbon();
        RefreshCommands();
    }

    private void HomeModern()
    {
        var editor = _editor!;
        var block = editor.SelectedBlock;
        var run = editor.ActiveRun;
        var styles = editor.Document.Styles;
        var styleIndex = block is null ? -1 : styles.FindIndex(value => value.Id.Equals(block.StyleId, StringComparison.OrdinalIgnoreCase));
        var style = Choice("Write.Home.Style", "Paragraph style", styles.Select(value => value.Name).ToArray(), Math.Max(0, styleIndex));
        style.SelectionChanged += (_, _) =>
        {
            if (style.SelectedIndex >= 0 && style.SelectedIndex < styles.Count) { editor.ApplyStyle(styles[style.SelectedIndex].Id); DocumentSurface.InvalidateDocument(); }
        };
        RibbonContent.Add(style);

        var fontIndex = Array.FindIndex(CommonFonts, value => value.Equals(run?.FontFamily, StringComparison.OrdinalIgnoreCase));
        var font = Choice("Write.Home.Font", "Font family", CommonFonts, Math.Max(0, fontIndex));
        font.SetValue(HavenProperties.MinWidth, HavenLength.Px(150));
        font.SelectionChanged += (_, _) => { if (font.SelectedItem is { } family) editor.SetFontFamily(family); };
        RibbonContent.Add(font);

        var currentSize = Math.Round(run?.FontSize ?? 14).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sizeIndex = Array.IndexOf(CommonSizes, currentSize);
        var size = Choice("Write.Home.Size", "Font size", CommonSizes, Math.Max(0, sizeIndex));
        size.SetValue(HavenProperties.MinWidth, HavenLength.Px(72));
        size.SelectionChanged += (_, _) => { if (double.TryParse(size.SelectedItem, out var points)) editor.SetFontSize(points); };
        RibbonContent.Add(size);

        RibbonContent.Add(Format("Write.Home.Bold", "B", run?.Bold == true, WriteCharacterFormat.Bold));
        RibbonContent.Add(Format("Write.Home.Italic", "I", run?.Italic == true, WriteCharacterFormat.Italic));
        RibbonContent.Add(Format("Write.Home.Underline", "U", run?.Underline == true, WriteCharacterFormat.Underline));
        RibbonContent.Add(Format("Write.Home.Strike", "S", run?.StrikeThrough == true, WriteCharacterFormat.StrikeThrough));

        foreach (var colour in new[] { ("Black", "#FF1C222A"), ("Blue", "#FF246BCE"), ("Red", "#FFD64545"), ("Green", "#FF238B57") })
        {
            var button = Btn("Write.Home.TextColour." + colour.Item1, colour.Item1);
            button.Accessibility.Description = "Set selected text colour to " + colour.Item1;
            button.Invoked += (_, _) => editor.SetForeground(colour.Item2);
            RibbonContent.Add(button);
        }
        foreach (var colour in new[] { ("No highlight", "#00000000"), ("Yellow", "#FFFFE66D"), ("Mint", "#FFBCEFD0"), ("Pink", "#FFFFC8DD") })
        {
            var button = Btn("Write.Home.Highlight." + colour.Item1.Replace(" ", string.Empty), colour.Item1);
            button.Accessibility.Description = "Set selected text highlight to " + colour.Item1;
            button.Invoked += (_, _) => editor.SetBackground(colour.Item2);
            RibbonContent.Add(button);
        }

        foreach (var alignment in new[] { NotesTextAlignment.Left, NotesTextAlignment.Center, NotesTextAlignment.Right, NotesTextAlignment.Justify })
        {
            var button = Btn("Write.Align." + alignment, alignment.ToString(), block?.Paragraph.Alignment == alignment ? ButtonVariant.Primary : ButtonVariant.Tertiary);
            button.Invoked += (_, _) => editor.SetAlignment(alignment);
            RibbonContent.Add(button);
        }

        var spacingValues = new[] { "1.0", "1.15", "1.5", "2.0" };
        var spacing = Choice("Write.Home.LineSpacing", "Line spacing", spacingValues, 0);
        spacing.SelectionChanged += (_, _) => { if (double.TryParse(spacing.SelectedItem, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) editor.SetLineSpacing(value); };
        RibbonContent.Add(spacing);
        var outdent = Btn("Write.Home.Outdent", "Outdent"); outdent.Invoked += (_, _) => editor.SetLeftIndent(Math.Max(0, (block?.Paragraph.IndentLeft ?? 0) - 24)); RibbonContent.Add(outdent);
        var indent = Btn("Write.Home.Indent", "Indent"); indent.Invoked += (_, _) => editor.SetLeftIndent((block?.Paragraph.IndentLeft ?? 0) + 24); RibbonContent.Add(indent);
        AddContextualAuthoringControls(block);
    }

    private void InsertModern()
    {
        AddInsert("Paragraph", NotesBlockKind.Paragraph);
        AddInsert("Heading 1", NotesBlockKind.Heading, style: "heading-1");
        AddInsert("Heading 2", NotesBlockKind.Heading, style: "heading-2");
        AddInsert("Quote", NotesBlockKind.Quote);
        AddInsert("Bullets", NotesBlockKind.List, NotesListKind.Bulleted);
        AddInsert("Numbering", NotesBlockKind.List, NotesListKind.Numbered);
        AddInsert("Checklist", NotesBlockKind.List, NotesListKind.Checklist);
        AddTableButton(2, 2); AddTableButton(3, 3); AddTableButton(4, 4);
        var image = Btn("Write.Insert.Image", "Image"); image.Invoked += (_, _) => ImageRequested?.Invoke(this, EventArgs.Empty); RibbonContent.Add(image);
        var shape = Btn("Write.Insert.CustomShape", "Shape"); shape.Invoked += (_, _) => { _editor?.InsertCustomShape(DocumentVectorShapes.CreateEditableStarter()); DocumentSurface.InvalidateDocument(); }; RibbonContent.Add(shape);
        AddInsert("Equation", NotesBlockKind.Equation);
        AddInsert("Divider", NotesBlockKind.Divider);
        var link = Field("Write.Insert.Link", "Link for selected text", "Paste link…");
        link.SetValue(HavenProperties.Width, HavenLength.Px(180));
        link.Invalidated += (_, _) => { if (!_suppress && _editor?.HasDocumentSelection == true) _editor.SetLink(link.Text); };
        RibbonContent.Add(link);
    }

    private void AddTableButton(int rows, int columns)
    {
        var button = Btn($"Write.Insert.Table{rows}x{columns}", $"Table {rows}×{columns}");
        button.Invoked += (_, _) =>
        {
            if (_editor is null) return;
            var block = _editor.InsertBlock(NotesBlockKind.Table);
            _editor.SelectBlock(block.Id);
            while (block.Table!.Rows.Count > rows) _editor.RemoveTableRow();
            while (block.Table.Rows.Count < rows) _editor.AddTableRow();
            while (block.Table.Rows[0].Cells.Count > columns) _editor.RemoveTableColumn();
            while (block.Table.Rows[0].Cells.Count < columns) _editor.AddTableColumn();
            DocumentSurface.InvalidateDocument();
        };
        RibbonContent.Add(button);
    }

    private void LayoutModern()
    {
        var editor = _editor!;
        var a4 = Btn("Write.Layout.A4", "A4"); a4.Invoked += (_, _) => editor.SetPagePreset("A4"); RibbonContent.Add(a4);
        var letter = Btn("Write.Layout.Letter", "Letter"); letter.Invoked += (_, _) => editor.SetPagePreset("Letter"); RibbonContent.Add(letter);
        foreach (var orientation in new[] { "Portrait", "Landscape" })
        {
            var button = Btn("Write.Layout." + orientation, orientation, editor.Document.PageSetup.Orientation.Equals(orientation, StringComparison.OrdinalIgnoreCase) ? ButtonVariant.Primary : ButtonVariant.Tertiary);
            button.Invoked += (_, _) => editor.SetOrientation(orientation); RibbonContent.Add(button);
        }
        var numbers = new Toggle { Name = "Write.Layout.PageNumbers", IsChecked = editor.Document.PageSetup.ShowPageNumbers }; numbers.Accessibility.AccessibleName = "Show page numbers"; numbers.CheckedChanged += (_, _) => { if (!_suppress) editor.SetPageNumbers(numbers.IsChecked); }; RibbonContent.Add(numbers);
        foreach (var mode in new[] { NotesLayoutMode.Paginated, NotesLayoutMode.Continuous })
        {
            var button = Btn("Write.Layout.Mode." + mode, mode.ToString(), editor.Document.LayoutMode == mode ? ButtonVariant.Primary : ButtonVariant.Tertiary);
            button.Invoked += (_, _) => editor.SetLayout(mode); RibbonContent.Add(button);
        }
        var zoomOut = Btn("Write.Zoom.Out", "−"); zoomOut.Accessibility.AccessibleName = "Zoom out"; zoomOut.Invoked += (_, _) => DocumentSurface.SetZoom(DocumentSurface.Zoom - .1); RibbonContent.Add(zoomOut);
        RibbonContent.Add(Caption($"{Math.Round(DocumentSurface.Zoom * 100)}%"));
        var zoomIn = Btn("Write.Zoom.In", "+"); zoomIn.Accessibility.AccessibleName = "Zoom in"; zoomIn.Invoked += (_, _) => DocumentSurface.SetZoom(DocumentSurface.Zoom + .1); RibbonContent.Add(zoomIn);
        var fit = Btn("Write.Zoom.Fit", "100%"); fit.Invoked += (_, _) => DocumentSurface.SetZoom(1); RibbonContent.Add(fit);
    }
}
