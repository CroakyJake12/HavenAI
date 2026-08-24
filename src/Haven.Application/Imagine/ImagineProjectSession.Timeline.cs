using Haven.Core;

namespace Haven.Application;

public sealed partial class ImagineProjectSession
{
    private const double MinimumClipDurationSeconds = 0.05d;

    public Guid AddTrack(ImagineTrackKind kind, string? name = null)
    {
        var id = Guid.NewGuid();
        var displayName = string.IsNullOrWhiteSpace(name)
            ? kind switch
            {
                ImagineTrackKind.Audio => "Audio track",
                ImagineTrackKind.Video => "Video track",
                _ => "Visual track"
            }
            : name.Trim();
        Apply(
            "add-track",
            new ImagineSelectionScope(ImagineSelectionKind.Track, id),
            "user",
            null,
            project => project with
            {
                Tracks = project.Tracks.Append(new ImagineTrack(id, kind, displayName, project.Tracks.Length, false, 1d, [])).ToArray(),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Track, id)
            });
        return id;
    }

    public bool DeleteTrack(Guid trackId)
    {
        if (Project.Tracks.All(track => track.Id != trackId)) return false;
        Apply(
            "delete-track",
            new ImagineSelectionScope(ImagineSelectionKind.Track, trackId),
            "user",
            null,
            project =>
            {
                var remaining = project.Tracks.Where(track => track.Id != trackId).OrderBy(track => track.Order).ToArray();
                for (var index = 0; index < remaining.Length; index++) remaining[index] = remaining[index] with { Order = index };
                return project with { Tracks = remaining, Selection = new ImagineSelectionScope(ImagineSelectionKind.None) };
            });
        return true;
    }

    public bool SelectTrack(Guid trackId)
    {
        if (Project.Tracks.All(track => track.Id != trackId)) return false;
        SetSelection(new ImagineSelectionScope(ImagineSelectionKind.Track, trackId));
        return true;
    }

    public bool SelectClip(Guid clipId)
    {
        if (FindClip(Project, clipId) is null) return false;
        SetSelection(new ImagineSelectionScope(ImagineSelectionKind.Clip, clipId));
        return true;
    }

    public void SelectTimelineRange(double startSeconds, double endSeconds)
    {
        var start = Math.Max(0, Math.Min(startSeconds, endSeconds));
        var end = Math.Max(start, Math.Max(startSeconds, endSeconds));
        SetSelection(new ImagineSelectionScope(ImagineSelectionKind.TimelineRange, null, null, start, end));
    }

    public bool MoveClip(Guid clipId, double timelineStartSeconds)
    {
        var location = FindClip(Project, clipId);
        if (location is null) return false;
        var start = Math.Max(0, timelineStartSeconds);
        if (NearlyEqual(location.Value.Clip.TimelineStartSeconds, start)) return false;
        Apply(
            "move-clip",
            new ImagineSelectionScope(ImagineSelectionKind.Clip, clipId),
            "user",
            null,
            project => project with
            {
                Tracks = ReplaceClip(project, location.Value.Track.Id, clipId, clip => clip with { TimelineStartSeconds = start }),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Clip, clipId)
            });
        return true;
    }

    public bool MoveClipToTrack(Guid clipId, Guid targetTrackId, double timelineStartSeconds)
    {
        var location = FindClip(Project, clipId);
        var target = Project.Tracks.FirstOrDefault(track => track.Id == targetTrackId);
        if (location is null || target is null || location.Value.Track.Kind != target.Kind) return false;
        if (location.Value.Track.Id == targetTrackId) return MoveClip(clipId, timelineStartSeconds);
        var moved = location.Value.Clip with { TimelineStartSeconds = Math.Max(0, timelineStartSeconds) };
        Apply(
            "move-clip-track",
            new ImagineSelectionScope(ImagineSelectionKind.Clip, clipId),
            "user",
            null,
            project => project with
            {
                Tracks = project.Tracks.Select(track =>
                    track.Id == location.Value.Track.Id
                        ? track with { Clips = track.Clips.Where(clip => clip.Id != clipId).ToArray() }
                        : track.Id == targetTrackId
                            ? track with { Clips = track.Clips.Append(moved).OrderBy(clip => clip.TimelineStartSeconds).ToArray() }
                            : track).ToArray(),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Clip, clipId)
            });
        return true;
    }

    public bool TrimClip(Guid clipId, double timelineStartSeconds, double sourceStartSeconds, double durationSeconds)
    {
        var location = FindClip(Project, clipId);
        if (location is null) return false;
        var timelineStart = Math.Max(0, timelineStartSeconds);
        var sourceStart = Math.Max(0, sourceStartSeconds);
        var duration = Math.Max(MinimumClipDurationSeconds, durationSeconds);
        var sourceDuration = AssetDurationSeconds(Project, location.Value.Clip.AssetId);
        if (sourceDuration is > MinimumClipDurationSeconds)
        {
            sourceStart = Math.Min(sourceStart, sourceDuration.Value - MinimumClipDurationSeconds);
            duration = Math.Min(duration, sourceDuration.Value - sourceStart);
        }
        var updated = location.Value.Clip with
        {
            TimelineStartSeconds = timelineStart,
            SourceStartSeconds = sourceStart,
            DurationSeconds = duration
        };
        if (updated == location.Value.Clip) return false;
        Apply(
            "trim-clip",
            new ImagineSelectionScope(ImagineSelectionKind.Clip, clipId),
            "user",
            null,
            project => project with
            {
                Tracks = ReplaceClip(project, location.Value.Track.Id, clipId, _ => updated),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Clip, clipId)
            });
        return true;
    }

    public bool SplitClip(Guid clipId, double timelineSeconds)
    {
        var location = FindClip(Project, clipId);
        if (location is null || location.Value.Clip.DurationSeconds <= MinimumClipDurationSeconds * 2) return false;
        var relative = timelineSeconds - location.Value.Clip.TimelineStartSeconds;
        if (relative <= MinimumClipDurationSeconds || relative >= location.Value.Clip.DurationSeconds - MinimumClipDurationSeconds) return false;
        var left = location.Value.Clip with { DurationSeconds = relative };
        var right = location.Value.Clip with
        {
            Id = Guid.NewGuid(),
            Name = location.Value.Clip.Name + " split",
            TimelineStartSeconds = timelineSeconds,
            SourceStartSeconds = location.Value.Clip.SourceStartSeconds + relative,
            DurationSeconds = location.Value.Clip.DurationSeconds - relative
        };
        Apply(
            "split-clip",
            new ImagineSelectionScope(ImagineSelectionKind.Clip, clipId),
            "user",
            null,
            project => project with
            {
                Tracks = project.Tracks.Select(track => track.Id == location.Value.Track.Id
                    ? track with
                    {
                        Clips = track.Clips.SelectMany(clip => clip.Id == clipId ? new[] { left, right } : new[] { clip })
                            .OrderBy(clip => clip.TimelineStartSeconds).ToArray()
                    }
                    : track).ToArray(),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Clip, right.Id)
            });
        return true;
    }

    public bool DeleteClip(Guid clipId)
    {
        var location = FindClip(Project, clipId);
        if (location is null) return false;
        Apply(
            "delete-clip",
            new ImagineSelectionScope(ImagineSelectionKind.Clip, clipId),
            "user",
            null,
            project => project with
            {
                Tracks = project.Tracks.Select(track => track.Id == location.Value.Track.Id
                    ? track with { Clips = track.Clips.Where(clip => clip.Id != clipId).ToArray() }
                    : track).ToArray(),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.None)
            });
        return true;
    }

    public bool SetTrackMuted(Guid trackId, bool muted) => UpdateTrack(trackId, "track-mute", track => track with { IsMuted = muted });

    public bool SetTrackGain(Guid trackId, double gain)
    {
        var value = Math.Clamp(gain, 0, 4);
        return UpdateTrack(trackId, "track-gain", track => track with { Gain = value });
    }

    public bool SetClipMuted(Guid clipId, bool muted) => UpdateClip(clipId, "clip-mute", clip => clip with { IsMuted = muted });

    public bool SetClipGain(Guid clipId, double gain)
    {
        var value = Math.Clamp(gain, 0, 4);
        return UpdateClip(clipId, "clip-gain", clip => clip with { Gain = value });
    }

    public bool ReorderTrack(Guid trackId, int targetIndex)
    {
        var ordered = Project.Tracks.OrderBy(track => track.Order).ToList();
        var currentIndex = ordered.FindIndex(track => track.Id == trackId);
        if (currentIndex < 0) return false;
        targetIndex = Math.Clamp(targetIndex, 0, ordered.Count - 1);
        if (currentIndex == targetIndex) return false;
        var item = ordered[currentIndex];
        ordered.RemoveAt(currentIndex);
        ordered.Insert(targetIndex, item);
        for (var index = 0; index < ordered.Count; index++) ordered[index] = ordered[index] with { Order = index };
        Apply(
            "reorder-track",
            new ImagineSelectionScope(ImagineSelectionKind.Track, trackId),
            "user",
            null,
            project => project with
            {
                Tracks = ordered.ToArray(),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Track, trackId)
            });
        return true;
    }

    private bool UpdateTrack(Guid trackId, string operation, Func<ImagineTrack, ImagineTrack> update)
    {
        var current = Project.Tracks.FirstOrDefault(track => track.Id == trackId);
        if (current is null) return false;
        var changed = update(current);
        if (changed == current) return false;
        Apply(
            operation,
            new ImagineSelectionScope(ImagineSelectionKind.Track, trackId),
            "user",
            null,
            project => project with
            {
                Tracks = project.Tracks.Select(track => track.Id == trackId ? changed : track).ToArray(),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Track, trackId)
            });
        return true;
    }

    private bool UpdateClip(Guid clipId, string operation, Func<ImagineClip, ImagineClip> update)
    {
        var location = FindClip(Project, clipId);
        if (location is null) return false;
        var changed = update(location.Value.Clip);
        if (changed == location.Value.Clip) return false;
        Apply(
            operation,
            new ImagineSelectionScope(ImagineSelectionKind.Clip, clipId),
            "user",
            null,
            project => project with
            {
                Tracks = ReplaceClip(project, location.Value.Track.Id, clipId, _ => changed),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Clip, clipId)
            });
        return true;
    }

    private static ImagineTrack[] ReplaceClip(ImagineProject project, Guid trackId, Guid clipId, Func<ImagineClip, ImagineClip> update) =>
        project.Tracks.Select(track => track.Id == trackId
            ? track with { Clips = track.Clips.Select(clip => clip.Id == clipId ? update(clip) : clip).ToArray() }
            : track).ToArray();

    private static ClipLocation? FindClip(ImagineProject project, Guid clipId)
    {
        foreach (var track in project.Tracks)
        {
            var clip = track.Clips.FirstOrDefault(item => item.Id == clipId);
            if (clip is not null) return new ClipLocation(track, clip);
        }
        return null;
    }

    private static double? AssetDurationSeconds(ImagineProject project, Guid assetId) =>
        AssetDurationSeconds(project.Assets.FirstOrDefault(item => item.Id == assetId));

    private static double? AssetDurationSeconds(ImagineMediaAsset? asset)
    {
        if (asset is null || string.IsNullOrWhiteSpace(asset.MetadataJson)) return null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(asset.MetadataJson);
            if (!document.RootElement.TryGetProperty("durationSeconds", out var duration)) return null;
            if (duration.ValueKind == System.Text.Json.JsonValueKind.Number && duration.TryGetDouble(out var numeric) && numeric > 0) return numeric;
            if (duration.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(duration.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out numeric) && numeric > 0) return numeric;
        }
        catch (System.Text.Json.JsonException) { }
        return null;
    }

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) < 0.0001d;

    private readonly record struct ClipLocation(ImagineTrack Track, ImagineClip Clip);
}
