using Haven.Core;

namespace Haven.Desktop.Views.Pages.Imagine;

internal sealed partial class ImagineTimelineElement
{
    private readonly ImagineAudioClipTransport _audioPreview = new();

    public bool PreviewSelectedAudio(out string status)
    {
        if (_session?.Project.Selection is not { Kind: ImagineSelectionKind.Clip, TargetId: Guid clipId })
        {
            status = "Select an audio clip first.";
            return false;
        }
        return _audioPreview.Play(_session.Project, clipId, SetPlayhead, out status);
    }

    public bool PauseOrResumeAudioPreview(out string status) => _audioPreview.PauseOrResume(out status);
    public bool StopAudioPreview(out string status) => _audioPreview.Stop(out status);
}
