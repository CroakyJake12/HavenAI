using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ImagineClipboardTests
{
    [Fact]
    public void Copy_paste_preserves_semantic_object_data_and_is_undoable()
    {
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Clipboard"));
        var originalId = session.AddRectangle(20, 30, 160, 90);
        Assert.True(session.SetSelectedFill("#123456"));
        Assert.True(session.CommitObjectTransform(originalId, new ImagineTransform(42, 64, 222, 111, 17)));

        Assert.True(session.CopySelected());
        Assert.True(session.PasteClipboard());

        Assert.Equal(2, session.Project.Objects.Length);
        var original = session.Project.Objects.Single(item => item.Id == originalId);
        var copy = session.Project.Objects.Single(item => item.Id != originalId);
        Assert.Equal(original.Kind, copy.Kind);
        Assert.Equal(original.Fill, copy.Fill);
        Assert.Equal(original.Text, copy.Text);
        Assert.Equal(original.MetadataJson, copy.MetadataJson);
        Assert.Equal(original.Transform.Width, copy.Transform.Width);
        Assert.Equal(original.Transform.Height, copy.Transform.Height);
        Assert.Equal(original.Transform.RotationDegrees, copy.Transform.RotationDegrees);
        Assert.Equal(original.Transform.X + 24, copy.Transform.X);
        Assert.Equal(original.Transform.Y + 24, copy.Transform.Y);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(copy.Id, session.Project.Selection.TargetId);
        Assert.Contains(session.Project.History, item => item.Operation == "paste-object");

        Assert.True(session.CommitObjectTransform(copy.Id, copy.Transform with { X = 500 }));
        Assert.Equal(42, session.Project.Objects.Single(item => item.Id == originalId).Transform.X);
        Assert.True(session.Undo());
        Assert.Equal(original.Transform.X + 24, session.Project.Objects.Single(item => item.Id == copy.Id).Transform.X);
        Assert.True(session.Undo());
        Assert.Single(session.Project.Objects);
    }

    [Fact]
    public void Image_copy_reuses_immutable_asset_and_keeps_sha_metadata()
    {
        var project = ImagineProjectSession.CreateProject("Asset clipboard");
        var session = new ImagineProjectSession(project);
        var asset = new ImagineMediaAsset(Guid.NewGuid(), ImagineMediaKind.Image, "generated.png", "source.png", "managed.png", 5, "abc123", DateTimeOffset.UtcNow, "{\"origin\":\"generated\"}");
        session.AddImportedAsset(asset);
        var originalId = session.Project.Selection.TargetId!.Value;

        Assert.True(session.CopySelected());
        Assert.True(session.PasteClipboard());

        Assert.Single(session.Project.Assets);
        var copy = session.Project.Objects.Single(item => item.Id != originalId);
        Assert.Equal(asset.Id, copy.AssetId);
        Assert.Equal("abc123", session.Project.Assets[0].Sha256);
        Assert.Equal(asset.MetadataJson, session.Project.Assets[0].MetadataJson);
    }

    [Fact]
    public void Cut_is_undoable_and_clipboard_can_paste_with_new_identity()
    {
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Cut"));
        var originalId = session.AddEllipse(40, 50, 120, 80);

        Assert.True(session.CutSelected());
        Assert.Empty(session.Project.Objects);
        Assert.True(session.Undo());
        Assert.Equal(originalId, Assert.Single(session.Project.Objects).Id);
        Assert.True(session.Redo());
        Assert.Empty(session.Project.Objects);
        Assert.True(session.PasteClipboard());

        var pasted = Assert.Single(session.Project.Objects);
        Assert.NotEqual(originalId, pasted.Id);
        Assert.False(pasted.IsLocked);
        Assert.Equal(ImagineSelectionKind.Object, session.Project.Selection.Kind);
        Assert.Equal(pasted.Id, session.Project.Selection.TargetId);
    }
}
