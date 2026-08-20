using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Haven.UI;
using Haven.UI.Components;
using HavenImageComponent = Haven.UI.Components.Image;

namespace Haven.Desktop.Views.Pages.Imagine;

/// <summary>
/// Keeps the image editor and media timelines in one responsive center slot while preserving
/// the real Imagine project/session model. Audio/video playback is never fabricated.
/// </summary>
internal sealed class ImagineMediaCanvasElement : ImagineCanvasElement, IHavenDrawCommandSource, IHavenPointerInputTarget, IHavenScrollInputTarget
{
    private readonly Text _notice;
    private readonly HavenImageComponent _videoPreview;
    private readonly ImagineTimelineElement _timeline;
    private readonly ImagineVideoFramePreviewService _videoFrames = new(new LocalMediaToolLocator());
    private ImagineMediaKind _mode = ImagineMediaKind.Image;
    private CancellationTokenSource? _videoPreviewCancellation;
    private string? _videoPreviewPath;

    public ImagineMediaCanvasElement()
    {
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Gap, HavenLength.Px(8));
        _notice = new Text { Name = "Imagine.Media.Notice", Content = string.Empty };
        _notice.SetValue(HavenProperties.Foreground, "TextSecondary");
        _notice.SetValue(HavenProperties.FontSize, 11d);
        _notice.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Add(_notice);
        _videoPreview = new HavenImageComponent { Name = "Imagine.Video.FramePreview", Source = string.Empty, Fit = HavenImageFit.Contain };
        _videoPreview.Accessibility.AccessibleName = "Decoded video frame preview";
        _videoPreview.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _videoPreview.SetValue(HavenProperties.Height, HavenLength.Px(260));
        _videoPreview.SetValue(HavenProperties.MinHeight, HavenLength.Px(180));
        _videoPreview.SetValue(HavenProperties.Background, "SurfaceRaised");
        _videoPreview.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        _videoPreview.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Add(_videoPreview);
        _timeline = new ImagineTimelineElement { Name = "Imagine.Timeline" };
        _timeline.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _timeline.SetValue(HavenProperties.Height, HavenLength.Fr(1));
        _timeline.SetValue(HavenProperties.MinHeight, HavenLength.Px(360));
        _timeline.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Add(_timeline);
    }

    public ImagineMediaKind Mode => _mode;
    public ImagineTimelineElement Timeline => _timeline;
    public Text Notice => _notice;
    public HavenImageComponent VideoPreview => _videoPreview;

    public new void SetSession(ImagineProjectSession session)
    {
        if (!ReferenceEquals(Session, session)) ClearVideoPreview();
        base.SetSession(session);
    }

    public void SetMode(ImagineMediaKind mode)
    {
        _mode = mode;
        var image = mode == ImagineMediaKind.Image;
        var video = mode == ImagineMediaKind.Video;
        _notice.SetValue(HavenProperties.Visibility, image ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        _videoPreview.SetValue(HavenProperties.Visibility, video ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        _timeline.SetValue(HavenProperties.Visibility, image ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        _timeline.SetValue(HavenProperties.MinHeight, HavenLength.Px(video ? 260 : 360));
        Accessibility.AccessibleName = mode switch
        {
            ImagineMediaKind.Audio => "Imagine audio multitrack workspace",
            ImagineMediaKind.Video => "Imagine video multitrack workspace",
            _ => "Imagine editable image canvas"
        };
        if (!image)
        {
            _notice.Content = mode == ImagineMediaKind.Audio
                ? "Real sampled waveforms appear for locally decodable audio. Selected audio clips can be previewed; full mixed-timeline playback is not yet available. Clip timing, trim, split, move, gain and mute are real edits."
                : "Select a video clip, move the playhead over it, then choose Preview frame for a real ffmpeg-decoded still. Continuous native video playback is not yet available. Timeline edits are real.";
            EnsureTimeline();
        }
        Invalidate();
    }

    public async Task<string> PreviewSelectedVideoFrameAsync(CancellationToken cancellationToken = default)
    {
        if (_mode != ImagineMediaKind.Video) return "Switch to Video before previewing a frame.";
        if (Session is null) return "Open an Imagine project before previewing video.";
        EnsureTimeline();
        if (Session.Project.Selection is not { Kind: ImagineSelectionKind.Clip, TargetId: Guid clipId })
            return "Select a video clip first.";

        _videoPreviewCancellation?.Cancel();
        _videoPreviewCancellation?.Dispose();
        var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _videoPreviewCancellation = request;
        try
        {
            var result = await _videoFrames.CreateFrameAsync(Session.Project, clipId, _timeline.PlayheadSeconds, request.Token);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Path)) return result.Status;
            var previous = _videoPreviewPath;
            _videoPreviewPath = result.Path;
            _videoPreview.Source = result.Path;
            if (!string.Equals(previous, result.Path, StringComparison.OrdinalIgnoreCase)) ImagineVideoFramePreviewService.DeleteTemporary(previous);
            return result.Status;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            return "Video frame preview cancelled.";
        }
        finally
        {
            if (ReferenceEquals(_videoPreviewCancellation, request)) _videoPreviewCancellation = null;
            request.Dispose();
        }
    }

    private void EnsureTimeline()
    {
        if (Session is null || _mode == ImagineMediaKind.Image) return;
        _timeline.SetSession(Session);
        _timeline.SetKind(_mode);
        _timeline.InvalidateTimeline();
    }

    private void ClearVideoPreview()
    {
        _videoPreviewCancellation?.Cancel();
        _videoPreviewCancellation?.Dispose();
        _videoPreviewCancellation = null;
        _videoPreview.Source = string.Empty;
        ImagineVideoFramePreviewService.DeleteTemporary(_videoPreviewPath);
        _videoPreviewPath = null;
    }

    void IHavenDrawCommandSource.Draw(HavenDrawingContext context, double opacity)
    {
        if (_mode == ImagineMediaKind.Image) { base.Draw(context, opacity); return; }
        EnsureTimeline();
    }

    bool IHavenPointerInputTarget.PointerPressed(HavenPointerInput input) => _mode == ImagineMediaKind.Image && base.PointerPressed(input);
    bool IHavenPointerInputTarget.PointerMoved(HavenPointerInput input) => _mode == ImagineMediaKind.Image && base.PointerMoved(input);
    bool IHavenPointerInputTarget.PointerReleased(HavenPointerInput input) => _mode == ImagineMediaKind.Image && base.PointerReleased(input);
    bool IHavenScrollInputTarget.PointerWheel(HavenPoint localPosition, double deltaX, double deltaY) => _mode == ImagineMediaKind.Image && base.PointerWheel(localPosition, deltaX, deltaY);

    public new void Dispose()
    {
        ClearVideoPreview();
        _timeline.Dispose();
        base.Dispose();
    }
}
