using System.Globalization;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Views.Pages.Present;

internal sealed partial class PresentHavenScene
{
    public event Action<string>? ThemePresetRequested;
    public event Action<PresentSlideSizePreset>? SlideSizePresetRequested;
    public event Action<Guid>? SlideLayoutRequested;
    public event Action<string>? SlideBackgroundRequested;
    public event Action<PresentTransitionKind>? TransitionKindRequested;
    public event Action<PresentEasingKind>? TransitionEasingRequested;
    public event Action<PresentMotionDirection>? TransitionDirectionRequested;
    public event Action<double>? TransitionDurationRequested;
    public event Action<PresentAnimationEffect, PresentAnimationTrigger>? AddAnimationRequested;
    public event EventHandler? RemoveAnimationRequested;
    public event Action<bool>? SlideHiddenRequested;
    public event Action<bool, bool, double, double?>? MediaPlaybackRequested;
    public event Action<string>? AlternativeTextRequested;

    public Container DesignPlaybackControls { get; private set; } = null!;
    public Select ThemeSelect { get; private set; } = null!;
    public Select SlideSizeSelect { get; private set; } = null!;
    public Select LayoutSelect { get; private set; } = null!;
    public Select TransitionSelect { get; private set; } = null!;
    public Select TransitionEasingSelect { get; private set; } = null!;
    public Select TransitionDirectionSelect { get; private set; } = null!;
    public Select TransitionDurationSelect { get; private set; } = null!;
    public Select AnimationEffectSelect { get; private set; } = null!;
    public Select AnimationTriggerSelect { get; private set; } = null!;
    public Toggle HiddenSlideToggle { get; private set; } = null!;
    public Toggle MediaAutoPlayToggle { get; private set; } = null!;
    public Toggle MediaLoopToggle { get; private set; } = null!;
    public Input MediaStartInput { get; private set; } = null!;
    public Input MediaEndInput { get; private set; } = null!;
    public Input AlternativeTextInput { get; private set; } = null!;
    public HavenButton AddAnimationButton { get; private set; } = null!;
    public HavenButton RemoveAnimationButton { get; private set; } = null!;

    private Guid[] _designLayoutIds = [];

    private void BuildDesignPlaybackControls()
    {
        DesignPlaybackControls = new Container { Name = "Present.DesignPlayback", Layout = HavenLayout.Vertical };
        DesignPlaybackControls.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        DesignPlaybackControls.SetValue(HavenProperties.Gap, HavenLength.Px(7));
        InspectorPane.Add(DesignPlaybackControls);

        DesignPlaybackControls.Add(Caption("Design"));
        ThemeSelect = NewInspectorSelect("Present.Design.Theme", "Theme", ["Haven", "Aurora", "Midnight", "Paper"]);
        SlideSizeSelect = NewInspectorSelect("Present.Design.Size", "Slide size", Enum.GetNames<PresentSlideSizePreset>());
        LayoutSelect = NewInspectorSelect("Present.Design.Layout", "Slide layout", []);
        DesignPlaybackControls.Add(ThemeSelect);
        DesignPlaybackControls.Add(SlideSizeSelect);
        DesignPlaybackControls.Add(LayoutSelect);

        var backgrounds = NewToolbar("Present.Design.Background", 0);
        backgrounds.Accessibility.AccessibleName = "Slide background presets";
        foreach (var preset in new[] { ("Theme", string.Empty), ("White", "#FFFFFF"), ("Warm", "#FFF7ED"), ("Dark", "#111827") })
        {
            var captured = preset;
            var button = NewButton("Present.Design.Background." + captured.Item1, captured.Item1);
            button.Invoked += (_, _) => SlideBackgroundRequested?.Invoke(captured.Item2);
            backgrounds.Add(button);
        }
        DesignPlaybackControls.Add(backgrounds);

        HiddenSlideToggle = new Toggle { Name = "Present.Design.HiddenSlide" };
        HiddenSlideToggle.Accessibility.AccessibleName = "Hide slide during presentation";
        HiddenSlideToggle.CheckedChanged += (_, _) => { if (!_suppressChanges) SlideHiddenRequested?.Invoke(HiddenSlideToggle.IsChecked); };
        DesignPlaybackControls.Add(HiddenSlideToggle);

        DesignPlaybackControls.Add(Caption("Transition"));
        TransitionSelect = NewInspectorSelect("Present.Transition.Kind", "Slide transition", Enum.GetNames<PresentTransitionKind>());
        TransitionEasingSelect = NewInspectorSelect("Present.Transition.Easing", "Transition easing", Enum.GetNames<PresentEasingKind>());
        TransitionDirectionSelect = NewInspectorSelect("Present.Transition.Direction", "Transition direction", Enum.GetNames<PresentMotionDirection>());
        TransitionDurationSelect = NewInspectorSelect("Present.Transition.Duration", "Transition duration", ["0.20 s", "0.35 s", "0.50 s", "0.75 s", "1.00 s"]);
        DesignPlaybackControls.Add(TransitionSelect);
        DesignPlaybackControls.Add(TransitionEasingSelect);
        DesignPlaybackControls.Add(TransitionDirectionSelect);
        DesignPlaybackControls.Add(TransitionDurationSelect);

        DesignPlaybackControls.Add(Caption("Animations"));
        AnimationEffectSelect = NewInspectorSelect("Present.Animation.Effect", "Animation effect", Enum.GetNames<PresentAnimationEffect>());
        AnimationTriggerSelect = NewInspectorSelect("Present.Animation.Trigger", "Animation trigger", Enum.GetNames<PresentAnimationTrigger>());
        DesignPlaybackControls.Add(AnimationEffectSelect);
        DesignPlaybackControls.Add(AnimationTriggerSelect);
        var animationActions = NewToolbar("Present.Animation.Actions", 0);
        AddAnimationButton = NewButton("Present.Animation.Add", "Add animation");
        RemoveAnimationButton = NewButton("Present.Animation.Remove", "Remove animations");
        animationActions.Add(AddAnimationButton);
        animationActions.Add(RemoveAnimationButton);
        DesignPlaybackControls.Add(animationActions);

        DesignPlaybackControls.Add(Caption("Accessibility & media"));
        AlternativeTextInput = new Input { Name = "Present.Accessibility.AltText", Placeholder = "Describe selected visualâ€¦" };
        AlternativeTextInput.Accessibility.AccessibleName = "Alternative text for selected visual";
        AlternativeTextInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        AlternativeTextInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
        DesignPlaybackControls.Add(AlternativeTextInput);

        var mediaToggles = NewToolbar("Present.Media.PlaybackToggles", 0);
        MediaAutoPlayToggle = new Toggle { Name = "Present.Media.AutoPlay" };
        MediaAutoPlayToggle.Accessibility.AccessibleName = "Autoplay selected media";
        MediaLoopToggle = new Toggle { Name = "Present.Media.Loop" };
        MediaLoopToggle.Accessibility.AccessibleName = "Loop selected media";
        mediaToggles.Add(MediaAutoPlayToggle);
        mediaToggles.Add(MediaLoopToggle);
        DesignPlaybackControls.Add(mediaToggles);
        var trim = NewToolbar("Present.Media.Trim", 0);
        MediaStartInput = NewInspectorInput("Present.Media.Start", "Media start time in seconds", "Start s");
        MediaEndInput = NewInspectorInput("Present.Media.End", "Media end time in seconds", "End s");
        trim.Add(MediaStartInput); trim.Add(MediaEndInput); DesignPlaybackControls.Add(trim);

        // Keep the final inspector controls fully scrollable above the fixed bottom status row.
        var bottomSafeArea = new Container { Name = "Present.Inspector.BottomSafeArea" };
        bottomSafeArea.SetValue(HavenProperties.Height, HavenLength.Px(56));
        bottomSafeArea.SetValue(HavenProperties.MinHeight, HavenLength.Px(56));
        bottomSafeArea.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        DesignPlaybackControls.Add(bottomSafeArea);

        ThemeSelect.SelectionChanged += (_, _) => { if (!_suppressChanges && ThemeSelect.SelectedItem is { } value) ThemePresetRequested?.Invoke(value); };
        SlideSizeSelect.SelectionChanged += (_, _) =>
        {
            if (!_suppressChanges && SlideSizeSelect.SelectedItem is { } value && Enum.TryParse<PresentSlideSizePreset>(value, out var preset)) SlideSizePresetRequested?.Invoke(preset);
        };
        LayoutSelect.SelectionChanged += (_, _) =>
        {
            if (!_suppressChanges && LayoutSelect.SelectedIndex >= 0 && LayoutSelect.SelectedIndex < _designLayoutIds.Length) SlideLayoutRequested?.Invoke(_designLayoutIds[LayoutSelect.SelectedIndex]);
        };
        TransitionSelect.SelectionChanged += (_, _) =>
        {
            if (!_suppressChanges && TransitionSelect.SelectedItem is { } value && Enum.TryParse<PresentTransitionKind>(value, out var kind)) TransitionKindRequested?.Invoke(kind);
        };
        TransitionEasingSelect.SelectionChanged += (_, _) =>
        {
            if (!_suppressChanges && TransitionEasingSelect.SelectedItem is { } value && Enum.TryParse<PresentEasingKind>(value, out var easing)) TransitionEasingRequested?.Invoke(easing);
        };
        TransitionDirectionSelect.SelectionChanged += (_, _) =>
        {
            if (!_suppressChanges && TransitionDirectionSelect.SelectedItem is { } value && Enum.TryParse<PresentMotionDirection>(value, out var direction)) TransitionDirectionRequested?.Invoke(direction);
        };
        TransitionDurationSelect.SelectionChanged += (_, _) =>
        {
            if (_suppressChanges || TransitionDurationSelect.SelectedItem is not { } value) return;
            var token = value.Replace(" s", string.Empty, StringComparison.Ordinal);
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)) TransitionDurationRequested?.Invoke(duration);
        };
        AddAnimationButton.Invoked += (_, _) =>
        {
            if (AnimationEffectSelect.SelectedItem is not { } effectText || AnimationTriggerSelect.SelectedItem is not { } triggerText) return;
            if (Enum.TryParse<PresentAnimationEffect>(effectText, out var effect) && Enum.TryParse<PresentAnimationTrigger>(triggerText, out var trigger)) AddAnimationRequested?.Invoke(effect, trigger);
        };
        RemoveAnimationButton.Invoked += (_, _) => RemoveAnimationRequested?.Invoke(this, EventArgs.Empty);
        AlternativeTextInput.Invalidated += (_, _) => { if (!_suppressChanges) AlternativeTextRequested?.Invoke(AlternativeTextInput.Text); };
        MediaAutoPlayToggle.CheckedChanged += (_, _) => EmitMediaPlaybackSettings();
        MediaLoopToggle.CheckedChanged += (_, _) => EmitMediaPlaybackSettings();
        MediaStartInput.Invalidated += (_, _) => EmitMediaPlaybackSettings();
        MediaEndInput.Invalidated += (_, _) => EmitMediaPlaybackSettings();
    }

    private void SetDesignPlaybackDocument(PresentDocument document, PresentSlide slide, IReadOnlyList<PresentElement> selectedElements)
    {
        var oldSuppress = _suppressChanges;
        _suppressChanges = true;
        try
        {
            ThemeSelect.SelectedIndex = Math.Max(0, Array.FindIndex(ThemeSelect.Items.ToArray(), item => item.Equals(document.Theme.Name, StringComparison.OrdinalIgnoreCase)));
            SlideSizeSelect.SelectedIndex = Array.IndexOf(Enum.GetNames<PresentSlideSizePreset>(), document.SlideSize.Preset.ToString());
            _designLayoutIds = document.Layouts.Select(layout => layout.Id).ToArray();
            LayoutSelect.Items = document.Layouts.Select(layout => layout.Name).ToArray();
            LayoutSelect.SelectedIndex = slide.LayoutId is { } layoutId ? Array.IndexOf(_designLayoutIds, layoutId) : -1;
            HiddenSlideToggle.IsChecked = slide.Hidden;
            TransitionSelect.SelectedIndex = (int)slide.Transition.Kind;
            TransitionEasingSelect.SelectedIndex = (int)slide.Transition.Easing;
            TransitionDirectionSelect.SelectedIndex = (int)slide.Transition.Direction;
            var durations = new[] { .2, .35, .5, .75, 1d };
            TransitionDurationSelect.SelectedIndex = NearestIndex(durations, slide.Transition.DurationSeconds <= 0 ? .35 : slide.Transition.DurationSeconds);
            if (AnimationEffectSelect.SelectedIndex < 0) AnimationEffectSelect.SelectedIndex = (int)PresentAnimationEffect.Fade;
            if (AnimationTriggerSelect.SelectedIndex < 0) AnimationTriggerSelect.SelectedIndex = (int)PresentAnimationTrigger.OnClick;

            var visual = selectedElements.Count == 1 && selectedElements[0].Kind is PresentElementKind.Image or PresentElementKind.Media or PresentElementKind.Shape ? selectedElements[0] : null;
            AlternativeTextInput.Text = visual?.AlternativeText ?? string.Empty;
            AlternativeTextInput.SetValue(HavenProperties.Enabled, visual is not null);

            var media = selectedElements.Count == 1 && selectedElements[0].Kind == PresentElementKind.Media ? selectedElements[0] : null;
            MediaAutoPlayToggle.IsChecked = media?.Media.AutoPlay == true;
            MediaLoopToggle.IsChecked = media?.Media.Loop == true;
            MediaStartInput.Text = media is null ? string.Empty : media.Media.StartSeconds.ToString("0.##", CultureInfo.InvariantCulture);
            MediaEndInput.Text = media?.Media.EndSeconds?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
            foreach (var control in new HavenElement[] { MediaAutoPlayToggle, MediaLoopToggle, MediaStartInput, MediaEndInput }) control.SetValue(HavenProperties.Enabled, media is not null);
            AddAnimationButton.SetValue(HavenProperties.Enabled, selectedElements.Count > 0);
            RemoveAnimationButton.SetValue(HavenProperties.Enabled, selectedElements.Any(element => slide.Animations.Any(cue => cue.TargetElementId == element.Id)));
        }
        finally
        {
            _suppressChanges = oldSuppress;
        }
    }

    private void EmitMediaPlaybackSettings()
    {
        if (_suppressChanges) return;
        _ = double.TryParse(MediaStartInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var start);
        double? end = double.TryParse(MediaEndInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedEnd) ? parsedEnd : null;
        MediaPlaybackRequested?.Invoke(MediaAutoPlayToggle.IsChecked, MediaLoopToggle.IsChecked, Math.Max(0, start), end);
    }

    private static Select NewInspectorSelect(string name, string accessibleName, IReadOnlyList<string> items)
    {
        var select = new Select { Name = name, Items = items };
        select.Accessibility.AccessibleName = accessibleName;
        select.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        select.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
        return select;
    }

    private static Input NewInspectorInput(string name, string accessibleName, string placeholder)
    {
        var input = new Input { Name = name, Placeholder = placeholder };
        input.Accessibility.AccessibleName = accessibleName;
        input.SetValue(HavenProperties.Width, HavenLength.Px(105));
        input.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
        return input;
    }

    private static int NearestIndex(IReadOnlyList<double> values, double target)
    {
        var best = 0; var distance = double.MaxValue;
        for (var index = 0; index < values.Count; index++)
        {
            var candidate = Math.Abs(values[index] - target);
            if (candidate >= distance) continue;
            distance = candidate; best = index;
        }
        return best;
    }
}
