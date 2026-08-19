using System.Globalization;
using Haven.Application;
using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Imagine;

internal sealed class ImagineTimelineElement : HavenElement, IHavenDrawCommandSource, IHavenPointerInputTarget, IHavenScrollInputTarget, IDisposable
{
    private const double HeaderWidth = 154d;
    private const double RulerHeight = 34d;
    private const double TrackHeight = 76d;
    private const double EdgeHitWidth = 7d;
    private enum Interaction { None, Scrub, Pan, Move, TrimStart, TrimEnd }

    private ImagineProjectSession? _session;
    private ImagineMediaKind _kind = ImagineMediaKind.Audio;
    private Interaction _interaction;
    private ImagineClip? _originalClip;
    private ImagineClip? _previewClip;
    private Guid? _originalTrackId;
    private Guid? _previewTrackId;
    private HavenPoint _pointerStart;
    private double _scrollSeconds;
    private int _trackScroll;
    private double _pixelsPerSecond = 72d;
    private double _playheadSeconds;

    public ImagineTimelineElement()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        Accessibility.Focusable = true;
        Accessibility.AccessibleName = "Imagine audio timeline";
        SetValue(HavenProperties.Background, "SurfaceRaised");
        SetValue(HavenProperties.BorderColor, "Border");
        SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        SetValue(HavenProperties.Clip, true);
    }

    public ImagineMediaKind Kind => _kind;
    public double PlayheadSeconds => _playheadSeconds;

    public void SetSession(ImagineProjectSession session)
    {
        if (ReferenceEquals(_session, session)) return;
        if (_session is not null) _session.Changed -= OnSessionChanged;
        _session = session;
        _session.Changed += OnSessionChanged;
        _scrollSeconds = 0;
        _trackScroll = 0;
        _playheadSeconds = 0;
        Invalidate();
    }

    public void SetKind(ImagineMediaKind kind)
    {
        if (kind == ImagineMediaKind.Image) throw new ArgumentOutOfRangeException(nameof(kind));
        _kind = kind;
        Accessibility.AccessibleName = kind == ImagineMediaKind.Audio ? "Imagine audio timeline" : "Imagine video timeline";
        _scrollSeconds = 0;
        _trackScroll = 0;
        Invalidate();
    }

    public void SetPlayhead(double seconds)
    {
        _playheadSeconds = Math.Max(0, seconds);
        Invalidate();
    }

    public Guid AddTrack() => AddTrack(_kind == ImagineMediaKind.Audio ? ImagineTrackKind.Audio : ImagineTrackKind.Video);

    public Guid AddTrack(ImagineTrackKind kind)
    {
        if (_session is null) return Guid.Empty;
        var allowed = _kind == ImagineMediaKind.Audio
            ? kind == ImagineTrackKind.Audio
            : kind is ImagineTrackKind.Audio or ImagineTrackKind.Video;
        return allowed ? _session.AddTrack(kind) : Guid.Empty;
    }

    public bool SplitSelected() => SelectedClipId() is Guid clipId && _session?.SplitClip(clipId, _playheadSeconds) == true;

    public bool DeleteSelected()
    {
        if (_session is null) return false;
        return _session.Project.Selection switch
        {
            { Kind: ImagineSelectionKind.Clip, TargetId: Guid clipId } => _session.DeleteClip(clipId),
            { Kind: ImagineSelectionKind.Track, TargetId: Guid trackId } => _session.DeleteTrack(trackId),
            _ => false
        };
    }

    public bool ToggleMuteSelected()
    {
        if (_session is null) return false;
        if (_session.Project.Selection is { Kind: ImagineSelectionKind.Clip, TargetId: Guid clipId } && FindClip(clipId) is { } clip)
            return _session.SetClipMuted(clipId, !clip.IsMuted);
        if (_session.Project.Selection is { Kind: ImagineSelectionKind.Track, TargetId: Guid trackId } && _session.Project.Tracks.FirstOrDefault(track => track.Id == trackId) is { } track)
            return _session.SetTrackMuted(trackId, !track.IsMuted);
        return false;
    }

    public bool AdjustGainSelected(double delta)
    {
        if (_session is null || !double.IsFinite(delta)) return false;
        if (_session.Project.Selection is { Kind: ImagineSelectionKind.Clip, TargetId: Guid clipId } && FindClip(clipId) is { } clip)
            return _session.SetClipGain(clipId, clip.Gain + delta);
        if (_session.Project.Selection is { Kind: ImagineSelectionKind.Track, TargetId: Guid trackId } && _session.Project.Tracks.FirstOrDefault(track => track.Id == trackId) is { } track)
            return _session.SetTrackGain(trackId, track.Gain + delta);
        return false;
    }

    public void ZoomBy(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) return;
        _pixelsPerSecond = Math.Clamp(_pixelsPerSecond * factor, 12, 320);
        Invalidate();
    }

    public void Fit()
    {
        if (_session is null || Bounds.Width <= HeaderWidth + 40) return;
        var duration = Math.Max(1, FilteredTracks().SelectMany(track => track.Clips).Select(clip => clip.TimelineStartSeconds + Math.Max(clip.DurationSeconds, 1)).DefaultIfEmpty(1).Max());
        _pixelsPerSecond = Math.Clamp((Bounds.Width - HeaderWidth - 24) / duration, 12, 240);
        _scrollSeconds = 0;
        Invalidate();
    }

    public void InvalidateTimeline() => Invalidate();

    public bool PointerPressed(HavenPointerInput input)
    {
        if (_session is null) return false;
        _pointerStart = input.LocalPosition;
        var time = TimeAt(input.LocalPosition.X);
        if (input.LocalPosition.Y <= RulerHeight)
        {
            _interaction = Interaction.Scrub;
            SetPlayhead(time);
            return true;
        }

        var track = TrackAt(input.LocalPosition.Y);
        if (track is null)
        {
            _interaction = Interaction.Pan;
            return true;
        }

        if (input.LocalPosition.X < HeaderWidth)
        {
            _session.SelectTrack(track.Id);
            _interaction = Interaction.None;
            return true;
        }

        var clip = track.Clips.OrderByDescending(item => item.TimelineStartSeconds).FirstOrDefault(item => ClipHit(item, time));
        if (clip is null)
        {
            _session.SelectTrack(track.Id);
            _interaction = Interaction.Pan;
            return true;
        }

        _session.SelectClip(clip.Id);
        _originalClip = clip;
        _previewClip = clip;
        _originalTrackId = track.Id;
        _previewTrackId = track.Id;
        var rect = ClipLocalRect(track, clip);
        _interaction = clip.DurationSeconds > 0 && Math.Abs(input.LocalPosition.X - rect.X) <= EdgeHitWidth
            ? Interaction.TrimStart
            : clip.DurationSeconds > 0 && Math.Abs(input.LocalPosition.X - rect.Right) <= EdgeHitWidth
                ? Interaction.TrimEnd
                : Interaction.Move;
        return true;
    }

    public bool PointerMoved(HavenPointerInput input)
    {
        if (_session is null) return false;
        var time = TimeAt(input.LocalPosition.X);
        if (_interaction == Interaction.Scrub)
        {
            SetPlayhead(time);
            return true;
        }
        if (_interaction == Interaction.Pan)
        {
            var delta = input.LocalPosition.X - _pointerStart.X;
            _scrollSeconds = Math.Max(0, _scrollSeconds - delta / Math.Max(12, _pixelsPerSecond));
            _pointerStart = input.LocalPosition;
            Invalidate();
            return true;
        }
        if (_originalClip is null) return false;

        var deltaSeconds = (input.LocalPosition.X - _pointerStart.X) / Math.Max(12, _pixelsPerSecond);
        if (_interaction == Interaction.Move)
        {
            _previewClip = _originalClip with { TimelineStartSeconds = Math.Max(0, _originalClip.TimelineStartSeconds + deltaSeconds) };
            var target = TrackAt(input.LocalPosition.Y);
            if (target is not null && TrackAccepts(target, _kind)) _previewTrackId = target.Id;
        }
        else if (_interaction == Interaction.TrimStart)
        {
            var delta = Math.Clamp(deltaSeconds, -_originalClip.SourceStartSeconds, _originalClip.DurationSeconds - .05);
            _previewClip = _originalClip with
            {
                TimelineStartSeconds = Math.Max(0, _originalClip.TimelineStartSeconds + delta),
                SourceStartSeconds = Math.Max(0, _originalClip.SourceStartSeconds + delta),
                DurationSeconds = Math.Max(.05, _originalClip.DurationSeconds - delta)
            };
        }
        else if (_interaction == Interaction.TrimEnd)
        {
            _previewClip = _originalClip with { DurationSeconds = Math.Max(.05, _originalClip.DurationSeconds + deltaSeconds) };
        }
        Invalidate();
        return true;
    }

    public bool PointerReleased(HavenPointerInput input)
    {
        if (_session is null) return false;
        if (_originalClip is not null && _previewClip is not null)
        {
            if (_interaction == Interaction.Move && _originalTrackId is Guid originalTrack && _previewTrackId is Guid previewTrack)
            {
                if (originalTrack == previewTrack) _session.MoveClip(_originalClip.Id, _previewClip.TimelineStartSeconds);
                else _session.MoveClipToTrack(_originalClip.Id, previewTrack, _previewClip.TimelineStartSeconds);
            }
            else if (_interaction is Interaction.TrimStart or Interaction.TrimEnd)
            {
                _session.TrimClip(_originalClip.Id, _previewClip.TimelineStartSeconds, _previewClip.SourceStartSeconds, _previewClip.DurationSeconds);
            }
        }
        _interaction = Interaction.None;
        _originalClip = null;
        _previewClip = null;
        _originalTrackId = null;
        _previewTrackId = null;
        Invalidate();
        return true;
    }

    public bool PointerWheel(HavenPoint localPosition, double deltaX, double deltaY)
    {
        var tracks = FilteredTracks();
        if (tracks.Count == 0) return false;
        var visible = Math.Max(1, (int)Math.Floor(Math.Max(0, Bounds.Height - RulerHeight) / TrackHeight));
        if (Math.Abs(deltaX) > Math.Abs(deltaY) && Math.Abs(deltaX) > .001)
            _scrollSeconds = Math.Max(0, _scrollSeconds + deltaX / Math.Max(12, _pixelsPerSecond));
        else if (tracks.Count > visible)
            _trackScroll = Math.Clamp(_trackScroll + (deltaY < 0 ? 1 : -1), 0, Math.Max(0, tracks.Count - visible));
        else
            _scrollSeconds = Math.Max(0, _scrollSeconds - deltaY / Math.Max(12, _pixelsPerSecond));
        Invalidate();
        return true;
    }

    public void Draw(HavenDrawingContext context, double opacity)
    {
        if (Bounds.Width <= 2 || Bounds.Height <= 2) return;
        context.Add(new HavenFillRoundedRectCommand(Bounds, new HavenTokenBrush("SurfaceRaised"), 14, opacity));
        DrawRuler(context, opacity);
        var y = Bounds.Y + RulerHeight;
        foreach (var track in FilteredTracks().Skip(_trackScroll))
        {
            if (y >= Bounds.Bottom) break;
            DrawTrack(context, track, y, opacity);
            y += TrackHeight;
        }
        var playheadX = Bounds.X + HeaderWidth + (_playheadSeconds - _scrollSeconds) * _pixelsPerSecond;
        if (playheadX >= Bounds.X + HeaderWidth && playheadX <= Bounds.Right)
        {
            var pen = new HavenPen(new HavenSolidBrush(255, 232, 74, 95), 2);
            context.Add(new HavenLineCommand(new HavenPoint(playheadX, Bounds.Y + 4), new HavenPoint(playheadX, Bounds.Bottom), pen, opacity));
        }
    }

    private void DrawRuler(HavenDrawingContext context, double opacity)
    {
        context.Add(new HavenFillRoundedRectCommand(new HavenRect(Bounds.X, Bounds.Y, Bounds.Width, RulerHeight), new HavenTokenBrush("Surface"), 12, opacity));
        var major = _pixelsPerSecond >= 120 ? 1d : _pixelsPerSecond >= 48 ? 2d : 5d;
        var first = Math.Floor(_scrollSeconds / major) * major;
        var pen = new HavenPen(new HavenTokenBrush("Border"), 1);
        for (var second = first; ; second += major)
        {
            var x = Bounds.X + HeaderWidth + (second - _scrollSeconds) * _pixelsPerSecond;
            if (x > Bounds.Right) break;
            if (x < Bounds.X + HeaderWidth) continue;
            context.Add(new HavenLineCommand(new HavenPoint(x, Bounds.Y + 20), new HavenPoint(x, Bounds.Y + RulerHeight), pen, opacity));
            context.Add(new HavenTextCommand(new HavenRect(x + 4, Bounds.Y + 3, 72, 18), new HavenTextLayout(FormatTime(second), "Segoe UI", 10, 600, 72), new HavenTokenBrush("TextSecondary"), opacity));
        }
    }

    private void DrawTrack(HavenDrawingContext context, ImagineTrack track, double y, double opacity)
    {
        var selectedTrack = _session?.Project.Selection is { Kind: ImagineSelectionKind.Track, TargetId: Guid selectedId } && selectedId == track.Id;
        var header = new HavenRect(Bounds.X, y, HeaderWidth, TrackHeight - 2);
        var lane = new HavenRect(Bounds.X + HeaderWidth, y, Math.Max(0, Bounds.Width - HeaderWidth), TrackHeight - 2);
        context.Add(new HavenFillRoundedRectCommand(header, new HavenTokenBrush(selectedTrack ? "AccentMuted" : "Surface"), 8, opacity));
        context.Add(new HavenFillRoundedRectCommand(lane, new HavenTokenBrush("SurfaceSubtle"), 6, opacity));
        context.Add(new HavenTextCommand(new HavenRect(header.X + 10, header.Y + 10, header.Width - 18, 22), new HavenTextLayout(track.Name, "Segoe UI", 12, 700, header.Width - 18, true), new HavenTokenBrush("TextPrimary"), opacity));
        var state = track.IsMuted ? "Muted" : $"Gain {track.Gain:0.##}";
        context.Add(new HavenTextCommand(new HavenRect(header.X + 10, header.Y + 38, header.Width - 18, 18), new HavenTextLayout(state, "Segoe UI", 10, 500, header.Width - 18), new HavenTokenBrush("TextSecondary"), opacity));
        foreach (var clip in track.Clips.OrderBy(item => item.TimelineStartSeconds)) DrawClip(context, track, clip, opacity);
    }

    private void DrawClip(HavenDrawingContext context, ImagineTrack track, ImagineClip clip, double opacity)
    {
        var shown = _previewClip is not null && _originalClip?.Id == clip.Id ? _previewClip : clip;
        var targetTrack = _previewClip is not null && _originalClip?.Id == clip.Id ? _previewTrackId : track.Id;
        if (targetTrack != track.Id) return;
        var local = ClipLocalRect(track, shown);
        var rect = new HavenRect(Bounds.X + local.X, Bounds.Y + local.Y, local.Width, local.Height);
        if (rect.Right < Bounds.X + HeaderWidth || rect.X > Bounds.Right) return;
        var selected = _session?.Project.Selection is { Kind: ImagineSelectionKind.Clip, TargetId: Guid selectedId } && selectedId == clip.Id;
        var brush = new HavenTokenBrush(selected ? "Accent" : track.Kind == ImagineTrackKind.Audio ? "AccentSecondary" : "AccentTertiary");
        context.Add(new HavenFillRoundedRectCommand(rect, brush, 8, opacity));
        if (selected) context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenTokenBrush("TextOnAccent"), 2), 8, opacity));
        var duration = shown.DurationSeconds > 0 ? FormatTime(shown.DurationSeconds) : "duration unknown";
        context.Add(new HavenTextCommand(new HavenRect(rect.X + 8, rect.Y + 8, Math.Max(0, rect.Width - 16), 20), new HavenTextLayout(shown.Name, "Segoe UI", 11, 700, Math.Max(20, rect.Width - 16), true), new HavenTokenBrush("TextOnAccent"), opacity));
        context.Add(new HavenTextCommand(new HavenRect(rect.X + 8, rect.Y + 31, Math.Max(0, rect.Width - 16), 18), new HavenTextLayout(duration + (shown.IsMuted ? " · muted" : string.Empty), "Segoe UI", 9, 500, Math.Max(20, rect.Width - 16), true), new HavenTokenBrush("TextOnAccent"), opacity));
    }

    internal IReadOnlyList<ImagineTrack> CurrentTracks => FilteredTracks();

    internal bool HitClipAtTime(ImagineClip clip, double time) => ClipHit(clip, time);

    private IReadOnlyList<ImagineTrack> FilteredTracks()
    {
        if (_session is null) return [];
        return _session.Project.Tracks
            .Where(track => _kind == ImagineMediaKind.Audio
                ? track.Kind == ImagineTrackKind.Audio
                : track.Kind is ImagineTrackKind.Video or ImagineTrackKind.Audio)
            .OrderBy(track => track.Order)
            .ToArray();
    }

    private ImagineTrack? TrackAt(double localY)
    {
        if (localY < RulerHeight) return null;
        var tracks = FilteredTracks();
        var index = _trackScroll + (int)Math.Floor((localY - RulerHeight) / TrackHeight);
        return index >= 0 && index < tracks.Count ? tracks[index] : null;
    }

    private HavenRect ClipLocalRect(ImagineTrack track, ImagineClip clip)
    {
        var tracks = FilteredTracks();
        var index = tracks.ToList().FindIndex(item => item.Id == track.Id) - _trackScroll;
        var y = RulerHeight + index * TrackHeight + 8;
        var x = HeaderWidth + (clip.TimelineStartSeconds - _scrollSeconds) * _pixelsPerSecond;
        var width = clip.DurationSeconds > 0 ? Math.Max(18, clip.DurationSeconds * _pixelsPerSecond) : 104;
        return new HavenRect(x, y, width, TrackHeight - 18);
    }

    private double TimeAt(double localX) => Math.Max(0, _scrollSeconds + (localX - HeaderWidth) / Math.Max(12, _pixelsPerSecond));
    private bool ClipHit(ImagineClip clip, double time) => clip.DurationSeconds > 0
        ? time >= clip.TimelineStartSeconds && time <= clip.TimelineStartSeconds + clip.DurationSeconds
        : time >= clip.TimelineStartSeconds && time <= clip.TimelineStartSeconds + 104 / Math.Max(12, _pixelsPerSecond);

    private bool TrackAccepts(ImagineTrack track, ImagineMediaKind kind)
    {
        if (_session is null || _originalTrackId is not Guid sourceTrackId) return false;
        var source = _session.Project.Tracks.FirstOrDefault(item => item.Id == sourceTrackId);
        return source is not null && source.Kind == track.Kind;
    }
    private Guid? SelectedClipId() => _session?.Project.Selection is { Kind: ImagineSelectionKind.Clip, TargetId: Guid id } ? id : null;
    private ImagineClip? FindClip(Guid id) => _session?.Project.Tracks.SelectMany(track => track.Clips).FirstOrDefault(clip => clip.Id == id);
    private static string FormatTime(double seconds) { var value = TimeSpan.FromSeconds(Math.Max(0, seconds)); return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : value.ToString(@"m\:ss", CultureInfo.InvariantCulture); }
    private void OnSessionChanged(object? sender, EventArgs e) => Invalidate();
    public void Dispose() { if (_session is not null) _session.Changed -= OnSessionChanged; }
}
