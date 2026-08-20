using Haven.Core;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Imagine;

/// <summary>
/// Keeps the image editor and media timelines in one responsive center slot while preserving
/// the real Imagine project/session model. Audio/video playback is never fabricated.
/// </summary>
internal sealed class ImagineMediaCanvasElement : ImagineCanvasElement, IHavenDrawCommandSource, IHavenPointerInputTarget, IHavenScrollInputTarget
{
    private readonly Text _notice;
    private readonly ImagineTimelineElement _timeline;
    private ImagineMediaKind _mode = ImagineMediaKind.Image;

    public ImagineMediaCanvasElement()
    {
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Gap, HavenLength.Px(8));
        _notice = new Text { Name = "Imagine.Media.Notice", Content = string.Empty };
        _notice.SetValue(HavenProperties.Foreground, "TextSecondary");
        _notice.SetValue(HavenProperties.FontSize, 11d);
        _notice.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Add(_notice);
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

    public void SetMode(ImagineMediaKind mode)
    {
        _mode = mode;
        var image = mode == ImagineMediaKind.Image;
        _notice.SetValue(HavenProperties.Visibility, image ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        _timeline.SetValue(HavenProperties.Visibility, image ? HavenVisibility.Collapsed : HavenVisibility.Visible);
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
                : "Video playback and preview are hidden because this runtime has no native video host. Timeline edits are real; known duration comes from local metadata.";
            EnsureTimeline();
        }
        Invalidate();
    }

    private void EnsureTimeline()
    {
        if (Session is null || _mode == ImagineMediaKind.Image) return;
        _timeline.SetSession(Session);
        _timeline.SetKind(_mode);
        _timeline.InvalidateTimeline();
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
        _timeline.Dispose();
        base.Dispose();
    }
}
