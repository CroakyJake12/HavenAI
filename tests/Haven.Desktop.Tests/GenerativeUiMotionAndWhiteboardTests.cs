using Avalonia;
using Avalonia.Headless.XUnit;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Components;

namespace Haven.Desktop.Tests;

public sealed class GenerativeUiMotionAndWhiteboardTests
{
    [AvaloniaFact]
    public void Streaming_preview_detects_template_early_and_builds_typed_skeleton_stages()
    {
        using var preview = new GenerativeUiStreamingPreview();
        const string opening = "```haven-ui\n{\"version\":1,\"template\":\"card-deck\",\"inputs\":{";

        Assert.True(GenerativeUiStreamingPreview.LooksLikeDirective(opening));
        preview.Update(opening);

        Assert.Equal("card-deck", preview.TemplateKey);
        Assert.True(preview.Stage >= 2);
        Assert.IsType<HavenLoadingState>(preview.Content);

        preview.Update(opening + new string(',', 10) + new string('x', 600));
        Assert.True(preview.Stage >= 3);
    }

    [Fact]
    public void Whiteboard_session_persists_stable_elements_and_undoable_object_edits()
    {
        var session = GeneratedWhiteboardControl.WhiteboardSession.Restore(null);
        session.CommitStroke(
        [
            new GeneratedWhiteboardControl.WhiteboardInkPoint(10, 10, 0.25),
            new GeneratedWhiteboardControl.WhiteboardInkPoint(40, 45, 0.9)
        ]);
        session.CommitShape(
            GeneratedWhiteboardControl.WhiteboardElementKind.Rectangle,
            new Point(60, 70),
            new Point(180, 150));
        session.CommitText(new Point(90, 170), "Study plan");

        var originalIds = session.Elements.Select(element => element.Id).ToArray();
        Assert.Equal(3, originalIds.Distinct().Count());
        Assert.True(session.SelectAt(new Point(100, 180)));
        session.CopySelected();
        session.Paste();
        Assert.Equal(4, session.Elements.Count);

        session.Undo();
        Assert.Equal(3, session.Elements.Count);
        session.Redo();
        Assert.Equal(4, session.Elements.Count);

        var restored = GeneratedWhiteboardControl.WhiteboardSession.Restore(session.ToJson());
        Assert.Equal(4, restored.Elements.Count);
        Assert.All(originalIds, id => Assert.Contains(restored.Elements, element => element.Id == id));
        Assert.Contains(restored.Elements, element => element.Kind == GeneratedWhiteboardControl.WhiteboardElementKind.Text);
        Assert.Equal(0.9, restored.Elements[0].Points[1].Pressure, 3);
    }
}
