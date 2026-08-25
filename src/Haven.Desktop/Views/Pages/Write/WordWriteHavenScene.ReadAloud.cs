using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Views.Pages.Write;

/// <summary>Read-aloud controls for the Write ribbon. Platform hosts resolve the controller.</summary>
internal sealed partial class WordWriteHavenScene
{
    private bool _readAloudActive;
    private bool _readAloudPaused;
    private string _readAloudSectionLabel = string.Empty;

    public event EventHandler? ReadAloudRequested;
    public event EventHandler? ReadAloudPauseResumeRequested;
    public event EventHandler? ReadAloudSkipBackRequested;
    public event EventHandler? ReadAloudSkipForwardRequested;
    public event EventHandler? ReadAloudStopRequested;

    /// <summary>
    /// Projects the controller state onto the Review ribbon. Controls appear while reading and the
    /// section caption always states exactly what is being spoken locally.
    /// </summary>
    public void SetReadAloudState(bool isActive, bool isPaused, string sectionLabel)
    {
        var rebuildNeeded = _tab == WordWriteRibbonTab.Review
            && (_readAloudActive != isActive
                || _readAloudPaused != isPaused
                || !string.Equals(_readAloudSectionLabel, sectionLabel, StringComparison.Ordinal));
        _readAloudActive = isActive;
        _readAloudPaused = isPaused;
        _readAloudSectionLabel = sectionLabel ?? string.Empty;
        if (rebuildNeeded)
            RebuildRibbon();
    }

    /// <summary>
    /// Adds the Read aloud group to the Review ribbon following the shared Btn/Caption helpers.
    /// </summary>
    private void AddReadAloudControls()
    {
        var start = Btn(
            "Write.Review.ReadAloud.Start",
            _readAloudActive ? "Restart read aloud" : "Read aloud",
            ButtonVariant.Primary);
        start.Accessibility.AccessibleName = _readAloudActive
            ? "Restart reading the document aloud"
            : "Read the document aloud";
        start.Invoked += (_, _) => ReadAloudRequested?.Invoke(this, EventArgs.Empty);
        RibbonContent.Add(start);

        if (!_readAloudActive)
            return;

        var back = Btn("Write.Review.ReadAloud.Back", "|< Back");
        back.Accessibility.AccessibleName = "Skip back one spoken section";
        back.Invoked += (_, _) => ReadAloudSkipBackRequested?.Invoke(this, EventArgs.Empty);
        RibbonContent.Add(back);

        var pauseResume = Btn(
            "Write.Review.ReadAloud.PauseResume",
            _readAloudPaused ? "> Resume" : "|| Pause");
        pauseResume.Accessibility.AccessibleName = _readAloudPaused
            ? "Resume reading aloud from the current section"
            : "Pause reading aloud at the current section";
        pauseResume.Invoked += (_, _) => ReadAloudPauseResumeRequested?.Invoke(this, EventArgs.Empty);
        RibbonContent.Add(pauseResume);

        var forward = Btn("Write.Review.ReadAloud.Forward", "Forward >|");
        forward.Accessibility.AccessibleName = "Skip forward one spoken section";
        forward.Invoked += (_, _) => ReadAloudSkipForwardRequested?.Invoke(this, EventArgs.Empty);
        RibbonContent.Add(forward);

        var stop = Btn("Write.Review.ReadAloud.Stop", "Stop", ButtonVariant.Danger);
        stop.Accessibility.AccessibleName = "Stop reading aloud";
        stop.Invoked += (_, _) => ReadAloudStopRequested?.Invoke(this, EventArgs.Empty);
        RibbonContent.Add(stop);

        var status = Caption(string.IsNullOrWhiteSpace(_readAloudSectionLabel)
            ? "Reading this document aloud locally."
            : _readAloudSectionLabel);
        status.Name = "Write.Review.ReadAloud.Status";
        RibbonContent.Add(status);
    }
}
