namespace Haven.Desktop.Views.Pages.Present;

public sealed partial class PresentPage
{
    private void InitializePresenterPlayback()
    {
        _route.PresentRequested += OnPresenterStarted;
        _route.PlaybackPreviousRequested += OnPresenterPreviousRequested;
        _route.PlaybackAdvanceRequested += OnPresenterAdvanceRequested;
        _route.PlaybackExitRequested += OnPresenterExitRequested;
    }

    private void DisposePresenterPlayback()
    {
        _route.PresentRequested -= OnPresenterStarted;
        _route.PlaybackPreviousRequested -= OnPresenterPreviousRequested;
        _route.PlaybackAdvanceRequested -= OnPresenterAdvanceRequested;
        _route.PlaybackExitRequested -= OnPresenterExitRequested;
    }

    private void OnPresenterStarted(object? sender, EventArgs e)
    {
        if (_playback is null || Document is null) return;
        _route.SetPresenterFrame(Document, _playback.Frame);
    }

    private void OnPresenterAdvanceRequested(object? sender, EventArgs e)
    {
        if (_playback is null || Document is null) return;
        if (!AdvancePlayback())
        {
            _route.SetStatus("End of presentation · Exit presenter to return to editing.");
            _route.SetPresenterFrame(Document, _playback.Frame);
            return;
        }
        _route.SetPresenterFrame(Document, _playback.Frame);
    }

    private void OnPresenterPreviousRequested(object? sender, EventArgs e)
    {
        if (_playback is null || Document is null) return;
        if (PreviousPlayback()) _route.SetPresenterFrame(Document, _playback.Frame);
    }

    private void OnPresenterExitRequested(object? sender, EventArgs e)
    {
        _playback = null;
        _route.SetPresenterVisible(false);
        _route.SetStatus(_dirty ? "Unsaved changes · autosave is on" : "Presentation editor ready.");
        RenderCurrent();
    }
}
