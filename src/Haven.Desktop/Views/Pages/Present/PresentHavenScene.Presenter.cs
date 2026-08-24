using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Present;

internal sealed partial class PresentHavenScene
{
    private static readonly JsonSerializerOptions PresenterJson = new(JsonSerializerDefaults.Web);

    public event EventHandler? PlaybackPreviousRequested;
    public event EventHandler? PlaybackAdvanceRequested;
    public event EventHandler? PlaybackExitRequested;

    public Container PresenterHost { get; private set; } = null!;
    public PresentSlideCanvas PresenterCanvas { get; private set; } = null!;
    public HavenText PresenterStatus { get; private set; } = null!;
    public HavenText PresenterNotes { get; private set; } = null!;
    public HavenButton PresenterPreviousButton { get; private set; } = null!;
    public HavenButton PresenterAdvanceButton { get; private set; } = null!;
    public HavenButton PresenterExitButton { get; private set; } = null!;

    private void BuildPresenterControls()
    {
        PresenterHost = new Container { Name = "Present.Playback.Host", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "48px 1fr Auto" };
        PresenterHost.SetValue(HavenProperties.Row, 0);
        PresenterHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        PresenterHost.SetValue(HavenProperties.MinHeight, HavenLength.Px(360));
        PresenterHost.SetValue(HavenProperties.Background, "SurfaceRaised");
        PresenterHost.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        PresenterHost.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        PresenterHost.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        PresenterHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        PresenterHost.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.ChildrenOnly);

        // Playback navigation stays in a fixed top row so it cannot be pushed below shorter viewports.
        var controls = NewToolbar("Present.Playback.Controls", 0);
        controls.SetValue(HavenProperties.Height, HavenLength.Px(48));
        controls.SetValue(HavenProperties.MinHeight, HavenLength.Px(48));
        PresenterPreviousButton = NewButton("Present.Playback.Previous", "Previous");
        PresenterAdvanceButton = NewButton("Present.Playback.Advance", "Advance");
        PresenterExitButton = NewButton("Present.Playback.Exit", "Exit presenter");
        PresenterAdvanceButton.Variant = ButtonVariant.Primary;
        controls.Add(PresenterPreviousButton);
        controls.Add(PresenterAdvanceButton);
        controls.Add(PresenterExitButton);
        PresenterHost.Add(controls);

        PresenterCanvas = new PresentSlideCanvas { Name = "Present.Playback.Canvas" };
        PresenterCanvas.SetValue(HavenProperties.Row, 1);
        PresenterCanvas.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        PresenterCanvas.SetValue(HavenProperties.MinHeight, HavenLength.Px(280));
        PresenterCanvas.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        PresenterHost.Add(PresenterCanvas);

        var info = new Container { Name = "Present.Playback.Info", Layout = HavenLayout.Grid, Columns = "1fr 1fr", Rows = "Auto" };
        info.SetValue(HavenProperties.Row, 2);
        info.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        PresenterStatus = new HavenText { Name = "Present.Playback.Status", Level = TextLevel.Caption };
        PresenterStatus.Accessibility.AccessibleName = "Presentation playback status";
        PresenterStatus.SetValue(HavenProperties.Foreground, "TextSecondary");
        info.Add(PresenterStatus);
        PresenterNotes = new HavenText { Name = "Present.Playback.Notes", Level = TextLevel.Caption };
        PresenterNotes.Accessibility.AccessibleName = "Speaker notes";
        PresenterNotes.SetValue(HavenProperties.Column, 1);
        PresenterNotes.SetValue(HavenProperties.Foreground, "TextSecondary");
        info.Add(PresenterNotes);
        PresenterHost.Add(info);

        PresenterPreviousButton.Invoked += (_, _) => PlaybackPreviousRequested?.Invoke(this, EventArgs.Empty);
        PresenterAdvanceButton.Invoked += (_, _) => PlaybackAdvanceRequested?.Invoke(this, EventArgs.Empty);
        PresenterExitButton.Invoked += (_, _) => PlaybackExitRequested?.Invoke(this, EventArgs.Empty);
        EditorHost.Add(PresenterHost);
    }

    public void SetPresenterVisible(bool visible)
    {
        PresenterHost.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        SlideCanvas.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        NotesHost.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        SlidePane.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        InspectorPane.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        Workspace.Columns = visible ? "0px 1fr 0px" : "190px 1fr 270px";
    }

    public void SetPresenterFrame(PresentDocument document, PresentPlaybackFrame frame)
    {
        var playbackSlide = BuildPlaybackSlide(frame);
        PresenterCanvas.SetSlide(document, playbackSlide, Array.Empty<Guid>());
        var totalAnimations = frame.Slide.Animations.Count;
        var transition = frame.Slide.Transition;
        PresenterStatus.Content = $"Slide {frame.SlideNumber} of {frame.SlideCount} Â· {(int)frame.Elapsed.TotalMinutes:00}:{frame.Elapsed.Seconds:00} Â· reveal {frame.AnimationStep}/{totalAnimations} Â· {transition.Kind} {transition.DurationSeconds:0.##}s";
        PresenterNotes.Content = string.IsNullOrWhiteSpace(frame.SpeakerNotes) ? "No speaker notes" : frame.SpeakerNotes;
        PresenterPreviousButton.SetValue(HavenProperties.Enabled, frame.SlideNumber > 1);
        PresenterAdvanceButton.Content = frame.SlideNumber == frame.SlideCount && frame.AnimationStep >= totalAnimations ? "End" : "Advance";
        SetPresenterVisible(true);
    }

    private static PresentSlide BuildPlaybackSlide(PresentPlaybackFrame frame)
    {
        var clone = JsonSerializer.Deserialize<PresentSlide>(JsonSerializer.Serialize(frame.Slide, PresenterJson), PresenterJson)
            ?? throw new InvalidDataException("Presentation slide could not be prepared for playback.");
        var animatedTargets = frame.Slide.Animations.Select(cue => cue.TargetElementId).ToHashSet();
        if (animatedTargets.Count == 0) return clone;
        var revealedTargets = frame.ActiveAnimations.Select(cue => cue.TargetElementId).ToHashSet();
        clone.Elements.RemoveAll(element => animatedTargets.Contains(element.Id) && !revealedTargets.Contains(element.Id));
        return clone;
    }
}
