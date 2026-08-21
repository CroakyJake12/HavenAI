using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Write;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class WriteDirectAuthoringTests
{
    [AvaloniaFact]
    public void Retained_document_surface_uses_document_contrast_and_full_layout_caret_metrics()
    {
        var document = NotesDocument.Create("Caret rendering");
        var paragraph = document.Sections[0].Pages[0].Blocks[0];
        paragraph.PlainText = string.Empty;
        paragraph.Runs = [new NotesTextRun { Text = string.Empty, Foreground = "#FFEEEEEE", FontFamily = "Montserrat", FontSize = 14 }];
        var typed = string.Join(' ', Enumerable.Repeat("proportional typing stays aligned with the rendered page", 9));

        using var scene = new WordWriteHavenScene();
        scene.SetDocument(document, 0, 1);
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 960, Height = 760, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            var router = new HavenInputRouter(scene.Root);
            router.Focus(scene.DocumentSurface);
            Assert.True(router.TextInput(typed));
            for (var index = 0; index < 5; index++)
                Assert.True(router.KeyDown(HavenKey.Left, new HavenInputModifiers(Shift: true)));

            var commands = new HavenSceneRenderer().Render(scene.Root);
            var text = Assert.Single(commands.OfType<HavenTextCommand>(), command => command.Layout.Text == typed);
            var brush = Assert.IsType<HavenSolidBrush>(text.Brush);
            Assert.True(brush.R < 80 && brush.G < 80 && brush.B < 80, $"Expected dark document text, got rgb({brush.R}, {brush.G}, {brush.B}).");

            var caret = Assert.Single(commands.OfType<HavenCaretCommand>(), command => command.FullLayout?.Text == typed);
            Assert.Equal(typed.Length - 5, caret.CaretIndex);
            var caretRect = HavenSceneControl.ResolveCaretRect(caret);
            Assert.True(caretRect.Y > caret.Rect.Y, "Expected the caret to follow the wrapped platform text layout.");
            Assert.InRange(caretRect.X, caret.Rect.X, caret.Rect.Right);

            var selection = Assert.Single(commands.OfType<HavenTextSelectionCommand>(), command => command.Layout.Text == typed);
            Assert.Equal(5, selection.SelectionLength);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Retained_document_surface_edits_table_cells_and_tabs_between_them()
    {
        var document = NotesDocument.Create("Direct table");
        var setup = new WriteDocumentEditor(document);
        var tableBlock = setup.InsertBlock(NotesBlockKind.Table);
        var first = tableBlock.Table!.Rows[0].Cells[0];
        var second = tableBlock.Table.Rows[0].Cells[1];
        first.Text = string.Empty;
        second.Text = string.Empty;

        using var scene = new WordWriteHavenScene();
        scene.SetDocument(document, 0, 1);
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1200, Height = 900, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.True(scene.DocumentSurface.TryGetTableCellBounds(first.Id, out var bounds));
            var point = new HavenPoint(bounds.X + 8, bounds.Y + 10);
            Assert.True(scene.DocumentSurface.PointerPressed(new HavenPointerInput(point, point, HavenPointerKind.Mouse)));
            Assert.Equal(first.Id, scene.DocumentSurface.ActiveTableCellId);
            Assert.True(scene.DocumentSurface.TextInput("Alpha"));
            Assert.Equal("Alpha", first.Text);

            Assert.True(scene.DocumentSurface.KeyDown(HavenKey.Tab, new HavenInputModifiers()));
            Assert.Equal(second.Id, scene.DocumentSurface.ActiveTableCellId);
            Assert.True(scene.DocumentSurface.TextInput("Beta"));
            Assert.Equal("Beta", second.Text);

            Assert.Contains(scene.RibbonContent.DescendantsAndSelf(), value => value.Name == "Write.Table.MergeRight");
            Assert.Contains(scene.RibbonContent.DescendantsAndSelf(), value => value.Name == "Write.Table.CellBackground.Bluecell");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Retained_document_surface_resizes_rotates_media_and_moves_vector_shapes()
    {
        var document = NotesDocument.Create("Direct objects");
        var setup = new WriteDocumentEditor(document);
        var mediaBlock = setup.InsertMedia(new NotesMediaData
        {
            OriginalName = "diagram.png",
            MediaType = "image/png",
            AltText = "Diagram",
            Width = 200,
            Height = 120
        });
        var shapeBlock = setup.InsertCustomShape(DocumentVectorShapes.CreateEditableStarter());

        using var scene = new WordWriteHavenScene();
        scene.SetDocument(document, 0, 1);
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1200, Height = 900, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.True(scene.DocumentSurface.TryGetBlockBounds(mediaBlock.Id, out var mediaBounds));
            var resizeStart = new HavenPoint(mediaBounds.Right - 3, mediaBounds.Bottom - 3);
            var resizeEnd = new HavenPoint(resizeStart.X + 48, resizeStart.Y + 28);
            Assert.True(scene.DocumentSurface.PointerPressed(new HavenPointerInput(resizeStart, resizeStart, HavenPointerKind.Mouse)));
            Assert.True(scene.DocumentSurface.PointerMoved(new HavenPointerInput(resizeEnd, resizeEnd, HavenPointerKind.Mouse)));
            Assert.True(scene.DocumentSurface.PointerReleased(new HavenPointerInput(resizeEnd, resizeEnd, HavenPointerKind.Mouse)));
            Assert.True(mediaBlock.Media!.Width > 200);
            Assert.True(mediaBlock.Media.Height > 120);

            Assert.True(scene.DocumentSurface.TryGetBlockBounds(mediaBlock.Id, out mediaBounds));
            var rotateStart = new HavenPoint(mediaBounds.Right - 3, mediaBounds.Bottom - 3);
            var rotateEnd = new HavenPoint(rotateStart.X + 40, rotateStart.Y);
            Assert.True(scene.DocumentSurface.PointerPressed(new HavenPointerInput(rotateStart, rotateStart, HavenPointerKind.Mouse)));
            Assert.True(scene.DocumentSurface.PointerMoved(new HavenPointerInput(rotateEnd, rotateEnd, HavenPointerKind.Mouse, HavenKeyModifiers.Shift)));
            Assert.True(scene.DocumentSurface.PointerReleased(new HavenPointerInput(rotateEnd, rotateEnd, HavenPointerKind.Mouse, HavenKeyModifiers.Shift)));
            Assert.NotEqual(0, mediaBlock.Media.Rotation);

            Assert.True(scene.DocumentSurface.TryGetBlockBounds(shapeBlock.Id, out var shapeBounds));
            var moveStart = new HavenPoint(shapeBounds.X + shapeBounds.Width / 2, shapeBounds.Y + shapeBounds.Height / 2);
            var moveEnd = new HavenPoint(moveStart.X + 32, moveStart.Y + 24);
            Assert.True(scene.DocumentSurface.PointerPressed(new HavenPointerInput(moveStart, moveStart, HavenPointerKind.Mouse)));
            Assert.True(scene.DocumentSurface.PointerMoved(new HavenPointerInput(moveEnd, moveEnd, HavenPointerKind.Mouse)));
            Assert.True(scene.DocumentSurface.PointerReleased(new HavenPointerInput(moveEnd, moveEnd, HavenPointerKind.Mouse)));
            Assert.True(Math.Abs(shapeBlock.VectorShape!.Transform.TranslateX) > .1);
            Assert.True(Math.Abs(shapeBlock.VectorShape.Transform.TranslateY) > .1);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void Table_merge_split_media_crop_and_transform_are_undoable_document_operations()
    {
        var document = NotesDocument.Create("Undo authoring");
        var editor = new WriteDocumentEditor(document);
        var table = editor.InsertBlock(NotesBlockKind.Table);
        editor.SelectBlock(table.Id);
        var first = table.Table!.Rows[0].Cells[0];
        var originalCellCount = table.Table.Rows[0].Cells.Count;

        Assert.True(editor.MergeTableCellRight(first.Id));
        Assert.Equal(originalCellCount - 1, table.Table.Rows[0].Cells.Count);
        Assert.True(first.ColumnSpan > 1);
        Assert.True(editor.SplitTableCell(first.Id));
        Assert.Equal(originalCellCount, table.Table.Rows[0].Cells.Count);

        var media = editor.InsertMedia(new NotesMediaData { Width = 400, Height = 300 });
        editor.SelectBlock(media.Id);
        Assert.True(editor.ResizeSelectedMedia(500, 350));
        Assert.True(editor.RotateSelectedMedia(30));
        Assert.True(editor.SetSelectedMediaCrop(.1, .05, .1, .05));
        Assert.Equal(500, media.Media!.Width);
        Assert.Equal(30, media.Media.Rotation);
        Assert.Equal(.1, media.Media.CropLeft, 3);
        Assert.True(editor.Undo());
        var restoredMedia = editor.Blocks().Single(block => block.Id == media.Id).Media!;
        Assert.Equal(0, restoredMedia.CropLeft);
    }

    [AvaloniaFact]
    public void Retained_document_surface_lays_out_more_than_thirty_pages_without_block_controls()
    {
        var document = NotesDocument.Create("Long document");
        document.LayoutMode = NotesLayoutMode.Paginated;
        var page = document.Sections[0].Pages[0];
        page.Blocks.Clear();
        for (var index = 0; index < 320; index++)
        {
            var block = NotesBlock.CreateParagraph($"Paragraph {index + 1}. {new string('x', 210)}");
            block.Order = index;
            page.Blocks.Add(block);
        }

        using var scene = new WordWriteHavenScene();
        scene.SetDocument(document, 0, 1);
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1200, Height = 900, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.True(scene.DocumentSurface.LaidOutPageCount >= 30, $"Expected at least 30 retained pages, got {scene.DocumentSurface.LaidOutPageCount}.");
            Assert.Empty(scene.BlockInputs);
            Assert.Single(scene.DocumentHost.Children);
            Assert.Same(scene.DocumentSurface, scene.DocumentHost.Children[0]);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }
}
