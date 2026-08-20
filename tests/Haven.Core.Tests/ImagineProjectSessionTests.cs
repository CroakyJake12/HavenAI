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
    public void Canvas_alignment_uses_rotated_visual_bounds_and_is_undoable()
    {
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Alignment", 1000, 800));
        session.AddRectangle(100, 100, 200, 100);
        Assert.True(session.RotateSelected(45));

        Assert.True(session.AlignSelectedHorizontal(0));
        var alignedLeft = Assert.Single(session.Project.Objects).Transform;
        var radians = alignedLeft.RotationDegrees * Math.PI / 180d;
        var visibleWidth = Math.Abs(alignedLeft.Width * Math.Cos(radians)) + Math.Abs(alignedLeft.Height * Math.Sin(radians));
        Assert.Equal(0, alignedLeft.X + alignedLeft.Width / 2 - visibleWidth / 2, 6);

        Assert.True(session.AlignSelectedVertical(1));
        var alignedBottom = Assert.Single(session.Project.Objects).Transform;
        var visibleHeight = Math.Abs(alignedBottom.Width * Math.Sin(radians)) + Math.Abs(alignedBottom.Height * Math.Cos(radians));
        Assert.Equal(800, alignedBottom.Y + alignedBottom.Height / 2 + visibleHeight / 2, 6);

        Assert.True(session.Undo());
        Assert.Equal(alignedLeft, Assert.Single(session.Project.Objects).Transform);
    }

    [Fact]
    public void Crop_canvas_to_rotated_selection_translates_composition_and_undo_restores_canvas()
    {
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Crop", 1000, 800));
        var selectedId = session.AddRectangle(200, 150, 200, 100);
        var otherId = session.AddRectangle(650, 500, 120, 90);
        Assert.True(session.SelectObject(selectedId));
        Assert.True(session.RotateSelected(30));
        var before = session.Project;
        var selectedBefore = before.Objects.Single(item => item.Id == selectedId).Transform;
        var otherBefore = before.Objects.Single(item => item.Id == otherId).Transform;
        var radians = selectedBefore.RotationDegrees * Math.PI / 180d;
        var expectedWidth = Math.Abs(selectedBefore.Width * Math.Cos(radians)) + Math.Abs(selectedBefore.Height * Math.Sin(radians));
        var expectedHeight = Math.Abs(selectedBefore.Width * Math.Sin(radians)) + Math.Abs(selectedBefore.Height * Math.Cos(radians));
        var expectedLeft = selectedBefore.X + selectedBefore.Width / 2 - expectedWidth / 2;
        var expectedTop = selectedBefore.Y + selectedBefore.Height / 2 - expectedHeight / 2;

        Assert.True(session.CropCanvasToSelection());

        Assert.Equal(expectedWidth, session.Project.CanvasWidth, 6);
        Assert.Equal(expectedHeight, session.Project.CanvasHeight, 6);
        var selectedAfter = session.Project.Objects.Single(item => item.Id == selectedId).Transform;
        var otherAfter = session.Project.Objects.Single(item => item.Id == otherId).Transform;
        Assert.Equal(selectedBefore.X - expectedLeft, selectedAfter.X, 6);
        Assert.Equal(selectedBefore.Y - expectedTop, selectedAfter.Y, 6);
        Assert.Equal(otherBefore.X - expectedLeft, otherAfter.X, 6);
        Assert.Equal(otherBefore.Y - expectedTop, otherAfter.Y, 6);

        Assert.True(session.Undo());
        Assert.Equal(1000, session.Project.CanvasWidth);
        Assert.Equal(800, session.Project.CanvasHeight);
        Assert.Equal(otherBefore, session.Project.Objects.Single(item => item.Id == otherId).Transform);
    }

    [Fact]
    public void Layer_visibility_lock_and_stacking_are_real_undoable_project_edits()
    {
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Layers", 1000, 800));
        var backId = session.AddRectangle(50, 50, 200, 120);
        var frontId = session.AddRectangle(90, 80, 200, 120);

        Assert.True(session.ToggleObjectVisibility(backId));
        Assert.False(session.Project.Objects.Single(item => item.Id == backId).IsVisible);
        Assert.True(session.Undo());
        Assert.True(session.Project.Objects.Single(item => item.Id == backId).IsVisible);

        Assert.True(session.ToggleObjectLock(frontId));
        Assert.True(session.Project.Objects.Single(item => item.Id == frontId).IsLocked);
        Assert.False(session.MoveObjectLayer(frontId, -1));
        Assert.True(session.ToggleObjectLock(frontId));
        Assert.False(session.Project.Objects.Single(item => item.Id == frontId).IsLocked);

        Assert.True(session.MoveObjectLayer(frontId, -1));
        var back = session.Project.Objects.Single(item => item.Id == backId);
        var front = session.Project.Objects.Single(item => item.Id == frontId);
        Assert.True(front.ZIndex < back.ZIndex);
        Assert.Equal(ImagineSelectionKind.Object, session.Project.Selection.Kind);
        Assert.Equal(frontId, session.Project.Selection.TargetId);
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
    public void Audio_and_video_imports_enter_shared_track_clip_model_with_truthful_known_durations()
    {
        var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Multimedia"));
        session.AddImportedAsset(new ImagineMediaAsset(
            Guid.NewGuid(), ImagineMediaKind.Audio, "voice.wav", "C:/voice.wav", "C:/managed.wav", 1, "a", DateTimeOffset.UtcNow, "{\"durationSeconds\":12.5}"));
        session.AddImportedAsset(new ImagineMediaAsset(
            Guid.NewGuid(), ImagineMediaKind.Video, "clip.mp4", "C:/clip.mp4", "C:/managed.mp4", 2, "b", DateTimeOffset.UtcNow, "{\"durationSeconds\":\"8\"}"));

        var audio = Assert.Single(session.Project.Tracks, item => item.Kind == ImagineTrackKind.Audio);
        var video = Assert.Single(session.Project.Tracks, item => item.Kind == ImagineTrackKind.Video);
        Assert.Equal(12.5, Assert.Single(audio.Clips).DurationSeconds);
        Assert.Equal(8, Assert.Single(video.Clips).DurationSeconds);
    }

    [Fact]
    public void Timeline_clip_split_move_trim_and_delete_are_real_undoable_edits()
    {
        var assetId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var clipId = Guid.NewGuid();
        var project = ImagineProjectSession.CreateProject("Timeline") with
        {
            Assets =
            [
                new ImagineMediaAsset(assetId, ImagineMediaKind.Audio, "voice.wav", "C:/voice.wav", "C:/managed.wav", 1, "a", DateTimeOffset.UtcNow, "{\"durationSeconds\":12}")
            ],
            Tracks =
            [
                new ImagineTrack(trackId, ImagineTrackKind.Audio, "Dialogue", 0, false, 1,
                    [new ImagineClip(clipId, assetId, "voice.wav", 0, 0, 12)])
            ]
        };
        var session = new ImagineProjectSession(project);

        Assert.True(session.SplitClip(clipId, 5));
        var clips = Assert.Single(session.Project.Tracks).Clips;
        Assert.Equal(2, clips.Length);
        Assert.Equal(5, clips[0].DurationSeconds);
        Assert.Equal(5, clips[1].SourceStartSeconds);
        Assert.Equal(7, clips[1].DurationSeconds);

        var second = clips[1];
        Assert.True(session.MoveClip(second.Id, 8));
        Assert.True(session.TrimClip(second.Id, 8, 6, 4));
        second = Assert.Single(session.Project.Tracks).Clips.Single(clip => clip.Id == second.Id);
        Assert.Equal(8, second.TimelineStartSeconds);
        Assert.Equal(6, second.SourceStartSeconds);
        Assert.Equal(4, second.DurationSeconds);

        Assert.True(session.DeleteClip(second.Id));
        Assert.Single(Assert.Single(session.Project.Tracks).Clips);
        Assert.True(session.Undo());
        Assert.Equal(2, Assert.Single(session.Project.Tracks).Clips.Length);
        Assert.Contains(session.Project.History, item => item.Operation == "undo");
    }

    [Fact]
    public void Timeline_supports_multitrack_move_mute_gain_and_reorder()
    {
        var assetId = Guid.NewGuid();
        var firstTrackId = Guid.NewGuid();
        var secondTrackId = Guid.NewGuid();
        var clipId = Guid.NewGuid();
        var project = ImagineProjectSession.CreateProject("Multitrack") with
        {
            Assets = [new ImagineMediaAsset(assetId, ImagineMediaKind.Audio, "music.wav", "C:/music.wav", "C:/managed.wav", 1, "m", DateTimeOffset.UtcNow)],
            Tracks =
            [
                new ImagineTrack(firstTrackId, ImagineTrackKind.Audio, "Music 1", 0, false, 1, [new ImagineClip(clipId, assetId, "music.wav", 0, 0, 8)]),
                new ImagineTrack(secondTrackId, ImagineTrackKind.Audio, "Music 2", 1, false, 1, [])
            ]
        };
        var session = new ImagineProjectSession(project);

        Assert.True(session.MoveClipToTrack(clipId, secondTrackId, 3));
        Assert.Empty(session.Project.Tracks.Single(track => track.Id == firstTrackId).Clips);
        Assert.Equal(3, Assert.Single(session.Project.Tracks.Single(track => track.Id == secondTrackId).Clips).TimelineStartSeconds);
        Assert.True(session.SetTrackMuted(secondTrackId, true));
        Assert.True(session.SetTrackGain(secondTrackId, 1.5));
        Assert.True(session.SetClipGain(clipId, .65));
        Assert.True(session.SetClipMuted(clipId, true));
        Assert.True(session.ReorderTrack(secondTrackId, 0));

        var first = session.Project.Tracks.Single(track => track.Id == secondTrackId);
        Assert.True(first.IsMuted);
        Assert.Equal(1.5, first.Gain);
        Assert.Equal(0, first.Order);
        var clip = Assert.Single(first.Clips);
        Assert.True(clip.IsMuted);
        Assert.Equal(.65, clip.Gain);
        Assert.Equal(ImagineSelectionKind.Track, session.Project.Selection.Kind);
    }
}
