using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ImagineProjectSessionTests
{
    [Fact]
    public void Image_import_creates_real_editable_object_and_undo_redo_restores_transform()
    {
        var project = ImagineProjectSession.CreateProject("Results Day", 1200, 800);
        var session = new ImagineProjectSession(project);
        var asset = new ImagineMediaAsset(
            Guid.NewGuid(), ImagineMediaKind.Image, "source.png", "C:/source.png", "C:/managed.png",
            1234, "abc", DateTimeOffset.UtcNow);

        session.AddImportedAsset(asset);

        var image = Assert.Single(session.Project.Objects);
        Assert.Equal(asset.Id, image.AssetId);
        var beforeMove = image.Transform;
        Assert.True(session.MoveSelected(90, 40));
        Assert.NotEqual(beforeMove, Assert.Single(session.Project.Objects).Transform);

        Assert.True(session.Undo());
        Assert.Equal(beforeMove, Assert.Single(session.Project.Objects).Transform);
        Assert.True(session.Redo());
        Assert.Equal(beforeMove.X + 90, Assert.Single(session.Project.Objects).Transform.X);
        Assert.Contains(session.Project.History, item => item.Operation == "redo");
    }

    [Fact]
    public void Semantic_components_keep_hierarchy_and_selected_ai_request_scope()
    {
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Semantic"));
        var asset = new ImagineMediaAsset(
            Guid.NewGuid(), ImagineMediaKind.Image, "portrait.png", "C:/source.png", "C:/managed.png",
            10, "hash", DateTimeOffset.UtcNow);
        session.AddImportedAsset(asset);
        var personId = Guid.NewGuid();
        var faceId = Guid.NewGuid();
        session.ReplaceSemanticComponents(asset.Id,
        [
            new ImagineSemanticComponent(personId, asset.Id, null, "person", "Person", "person", new ImagineRegion(.1, .1, .8, .8), 0, null, .96, "vision-bounds", "vision"),
            new ImagineSemanticComponent(faceId, asset.Id, personId, "face", "Face", "face", new ImagineRegion(.3, .15, .4, .3), 1, null, .91, "vision-bounds", "vision")
        ], "vision");

        Assert.True(session.SelectSemanticComponent(faceId));
        var request = session.CreateAiEditRequest("Make this selection more dramatic.");

        Assert.Equal(ImagineSelectionKind.SemanticComponent, request.Scope.Kind);
        Assert.Equal(faceId, request.Scope.TargetId);
        Assert.Equal(new[] { asset.Id }, request.AssetIds);
        Assert.Equal(personId, session.Project.SemanticComponents.Single(item => item.Id == faceId).ParentId);
        Assert.All(session.Project.SemanticComponents, item => Assert.Null(item.MaskPath));
    }

    [Fact]
    public void Audio_and_video_imports_enter_shared_track_clip_model_without_fake_editor_controls()
    {
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Multimedia"));
        session.AddImportedAsset(new ImagineMediaAsset(
            Guid.NewGuid(), ImagineMediaKind.Audio, "voice.wav", "C:/voice.wav", "C:/managed.wav", 1, "a", DateTimeOffset.UtcNow));
        session.AddImportedAsset(new ImagineMediaAsset(
            Guid.NewGuid(), ImagineMediaKind.Video, "clip.mp4", "C:/clip.mp4", "C:/managed.mp4", 2, "b", DateTimeOffset.UtcNow));

        Assert.Contains(session.Project.Tracks, item => item.Kind == ImagineTrackKind.Audio);
        Assert.Contains(session.Project.Tracks, item => item.Kind == ImagineTrackKind.Video);
        Assert.All(session.Project.Tracks, track => Assert.Single(track.Clips));
    }
}
