using Haven.Core;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Write;

internal sealed partial class WordWriteHavenScene
{
    private void AddContextualAuthoringControls(NotesBlock? block)
    {
        if (_editor is null || block is null) return;
        switch (block.Kind)
        {
            case NotesBlockKind.Table when block.Table is not null:
                AddTableContextControls(block);
                break;
            case NotesBlockKind.Image or NotesBlockKind.Audio or NotesBlockKind.Video when block.Media is not null:
                AddMediaContextControls(block);
                break;
            case NotesBlockKind.Shape when block.VectorShape is not null:
                AddVectorContextControls(block);
                break;
        }
    }

    private void AddTableContextControls(NotesBlock block)
    {
        var editor = _editor!;
        RibbonContent.Add(Caption("Table"));
        AddContextButton("Write.Table.AddRow", "+ Row", () => editor.AddTableRow());
        AddContextButton("Write.Table.RemoveRow", "− Row", () => editor.RemoveTableRow());
        AddContextButton("Write.Table.AddColumn", "+ Column", () => editor.AddTableColumn());
        AddContextButton("Write.Table.RemoveColumn", "− Column", () => editor.RemoveTableColumn());

        if (DocumentSurface.ActiveTableCellId is not { } cellId) return;
        var cell = block.Table!.Rows.SelectMany(row => row.Cells).FirstOrDefault(value => value.Id == cellId);
        if (cell is null) return;

        AddContextButton("Write.Table.MergeRight", "Merge right", () => editor.MergeTableCellRight(cellId));
        AddContextButton("Write.Table.SplitCell", "Split cell", () => editor.SplitTableCell(cellId));
        foreach (var colour in new[]
        {
            ("Clear", "#00000000"),
            ("Blue cell", "#FFEAF2FF"),
            ("Gold cell", "#FFFFF1C7"),
            ("Mint cell", "#FFE3F7EC")
        })
        {
            var captured = colour;
            AddContextButton("Write.Table.CellBackground." + captured.Item1.Replace(" ", string.Empty), captured.Item1,
                () => editor.SetTableCellBackground(cellId, captured.Item2));
        }
        RibbonContent.Add(Caption($"Cell span {Math.Max(1, cell.RowSpan)}×{Math.Max(1, cell.ColumnSpan)}"));
    }

    private void AddMediaContextControls(NotesBlock block)
    {
        var editor = _editor!;
        var media = block.Media!;
        RibbonContent.Add(Caption("Media"));

        var wrapItems = new[] { "Inline", "Square", "Tight", "Behind text", "In front of text" };
        var wrap = Choice("Write.Media.Wrap", "Text wrapping", wrapItems, Math.Max(0, Array.IndexOf(wrapItems, media.Wrapping)));
        wrap.SelectionChanged += (_, _) =>
        {
            if (_suppress || wrap.SelectedItem is null) return;
            editor.UpdateMedia(media.AltText, media.Caption, wrap.SelectedItem);
            DocumentSurface.InvalidateDocument();
        };
        RibbonContent.Add(wrap);

        var alt = Field("Write.Media.Alt", "Image alternative text", "Describe image…");
        alt.Text = media.AltText; alt.SetValue(HavenProperties.Width, HavenLength.Px(180));
        alt.Invalidated += (_, _) => { if (!_suppress && alt.Text != media.AltText) editor.UpdateMedia(alt.Text, media.Caption, media.Wrapping); };
        RibbonContent.Add(alt);

        var caption = Field("Write.Media.Caption", "Image caption", "Caption…");
        caption.Text = media.Caption; caption.SetValue(HavenProperties.Width, HavenLength.Px(160));
        caption.Invalidated += (_, _) => { if (!_suppress && caption.Text != media.Caption) editor.UpdateMedia(media.AltText, caption.Text, media.Wrapping); };
        RibbonContent.Add(caption);

        AddContextButton("Write.Media.Smaller", "90%", () => editor.ResizeSelectedMedia(media.Width * .9, media.Height * .9));
        AddContextButton("Write.Media.Larger", "110%", () => editor.ResizeSelectedMedia(media.Width * 1.1, media.Height * 1.1));
        AddContextButton("Write.Media.RotateLeft", "Rotate −15°", () => editor.RotateSelectedMedia(-15));
        AddContextButton("Write.Media.RotateRight", "Rotate +15°", () => editor.RotateSelectedMedia(15));
        AddContextButton("Write.Media.CropReset", "Reset crop", () => editor.SetSelectedMediaCrop(0, 0, 0, 0));
        AddContextButton("Write.Media.Crop5", "Crop 5%", () => editor.SetSelectedMediaCrop(.05, .05, .05, .05));
        AddContextButton("Write.Media.Crop10", "Crop 10%", () => editor.SetSelectedMediaCrop(.1, .1, .1, .1));
        RibbonContent.Add(Caption($"{media.Width:0}×{media.Height:0} · {media.Rotation:0.#}°"));
    }

    private void AddVectorContextControls(NotesBlock block)
    {
        var editor = _editor!;
        var shape = block.VectorShape!;
        RibbonContent.Add(Caption("Shape"));
        AddContextButton("Write.Shape.RotateLeft", "Rotate −15°", () => TransformShape(editor, shape, rotation: -15));
        AddContextButton("Write.Shape.RotateRight", "Rotate +15°", () => TransformShape(editor, shape, rotation: 15));
        AddContextButton("Write.Shape.Smaller", "90%", () => TransformShape(editor, shape, scale: .9));
        AddContextButton("Write.Shape.Larger", "110%", () => TransformShape(editor, shape, scale: 1.1));
        RibbonContent.Add(Caption($"{shape.Name} · {shape.Transform.RotationDegrees:0.#}°"));
    }

    private void AddContextButton(string name, string label, Func<bool> mutate)
    {
        var button = Btn(name, label);
        button.Invoked += (_, _) =>
        {
            if (!mutate()) return;
            DocumentSurface.InvalidateDocument();
            RebuildRibbon();
        };
        RibbonContent.Add(button);
    }

    private void AddContextButton(string name, string label, Action mutate)
    {
        var button = Btn(name, label);
        button.Invoked += (_, _) =>
        {
            mutate();
            DocumentSurface.InvalidateDocument();
            RebuildRibbon();
        };
        RibbonContent.Add(button);
    }

    private static bool TransformShape(Haven.Application.WriteDocumentEditor editor, DocumentVectorShape shape, double rotation = 0, double scale = 1)
    {
        var current = shape.Transform;
        return editor.UpdateSelectedCustomShape(value => value.SetTransform(new DocumentVectorTransform
        {
            TranslateX = current.TranslateX,
            TranslateY = current.TranslateY,
            ScaleX = current.ScaleX * scale,
            ScaleY = current.ScaleY * scale,
            RotationDegrees = current.RotationDegrees + rotation,
            OriginX = current.OriginX,
            OriginY = current.OriginY
        }));
    }
}
