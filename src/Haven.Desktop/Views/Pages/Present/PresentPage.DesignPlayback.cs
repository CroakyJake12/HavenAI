using Haven.Core;

namespace Haven.Desktop.Views.Pages.Present;

public sealed partial class PresentPage
{
    private void InitializeDesignPlayback()
    {
        _route.ThemePresetRequested += OnThemePresetRequested;
        _route.SlideSizePresetRequested += OnSlideSizePresetRequested;
        _route.SlideLayoutRequested += OnSlideLayoutRequested;
        _route.SlideBackgroundRequested += OnSlideBackgroundRequested;
        _route.TransitionKindRequested += OnTransitionKindRequested;
        _route.TransitionEasingRequested += OnTransitionEasingRequested;
        _route.TransitionDirectionRequested += OnTransitionDirectionRequested;
        _route.TransitionDurationRequested += OnTransitionDurationRequested;
        _route.AddAnimationRequested += OnAddAnimationRequested;
        _route.RemoveAnimationRequested += OnRemoveAnimationRequested;
        _route.SlideHiddenRequested += OnSlideHiddenRequested;
        _route.MediaPlaybackRequested += OnMediaPlaybackRequested;
        _route.AlternativeTextRequested += OnAlternativeTextRequested;
        InitializePresenterPlayback();
    }

    private void DisposeDesignPlayback()
    {
        _route.ThemePresetRequested -= OnThemePresetRequested;
        _route.SlideSizePresetRequested -= OnSlideSizePresetRequested;
        _route.SlideLayoutRequested -= OnSlideLayoutRequested;
        _route.SlideBackgroundRequested -= OnSlideBackgroundRequested;
        _route.TransitionKindRequested -= OnTransitionKindRequested;
        _route.TransitionEasingRequested -= OnTransitionEasingRequested;
        _route.TransitionDirectionRequested -= OnTransitionDirectionRequested;
        _route.TransitionDurationRequested -= OnTransitionDurationRequested;
        _route.AddAnimationRequested -= OnAddAnimationRequested;
        _route.RemoveAnimationRequested -= OnRemoveAnimationRequested;
        _route.SlideHiddenRequested -= OnSlideHiddenRequested;
        _route.MediaPlaybackRequested -= OnMediaPlaybackRequested;
        _route.AlternativeTextRequested -= OnAlternativeTextRequested;
        DisposePresenterPlayback();
    }

    private void OnThemePresetRequested(string preset) => _editor?.ApplyThemePreset(preset);
    private void OnSlideSizePresetRequested(PresentSlideSizePreset preset) => _editor?.SetSlideSizePreset(preset);

    private void OnSlideLayoutRequested(Guid layoutId)
    {
        if (_editor is null || CurrentSlide is not { } slide) return;
        _editor.SetSlideLayout(slide.Id, layoutId);
    }

    private void OnSlideBackgroundRequested(string color)
    {
        if (_editor is null || CurrentSlide is not { } slide) return;
        _editor.SetSlideBackgroundColor(slide.Id, color);
    }

    private void OnTransitionKindRequested(PresentTransitionKind kind)
    {
        if (_editor is null || CurrentSlide is not { } slide) return;
        var transition = slide.Transition;
        _editor.SetSlideTransition(slide.Id, kind, EffectiveTransitionDuration(transition), transition.Easing, transition.Direction);
    }

    private void OnTransitionEasingRequested(PresentEasingKind easing)
    {
        if (_editor is null || CurrentSlide is not { } slide) return;
        var transition = slide.Transition;
        _editor.SetSlideTransition(slide.Id, transition.Kind, EffectiveTransitionDuration(transition), easing, transition.Direction);
    }

    private void OnTransitionDirectionRequested(PresentMotionDirection direction)
    {
        if (_editor is null || CurrentSlide is not { } slide) return;
        var transition = slide.Transition;
        _editor.SetSlideTransition(slide.Id, transition.Kind, EffectiveTransitionDuration(transition), transition.Easing, direction);
    }

    private void OnTransitionDurationRequested(double duration)
    {
        if (_editor is null || CurrentSlide is not { } slide) return;
        var transition = slide.Transition;
        _editor.SetSlideTransition(slide.Id, transition.Kind, duration, transition.Easing, transition.Direction);
    }

    private void OnAddAnimationRequested(PresentAnimationEffect effect, PresentAnimationTrigger trigger)
    {
        if (_editor is null) return;
        var direction = effect == PresentAnimationEffect.Fly ? PresentMotionDirection.Up : PresentMotionDirection.None;
        _editor.AddAnimationToSelection(effect, trigger, .35, direction);
    }

    private void OnRemoveAnimationRequested(object? sender, EventArgs e) => _editor?.RemoveAnimationsFromSelection();

    private void OnSlideHiddenRequested(bool hidden)
    {
        if (_editor is null || CurrentSlide is not { } slide) return;
        _editor.SetSlideHidden(slide.Id, hidden);
    }

    private void OnMediaPlaybackRequested(bool autoPlay, bool loop, double start, double? end) =>
        _editor?.SetSelectedMediaPlayback(autoPlay, loop, start, end);

    private void OnAlternativeTextRequested(string alternativeText) => _editor?.SetSelectedAlternativeText(alternativeText);

    private static double EffectiveTransitionDuration(PresentTransition transition) =>
        transition.DurationSeconds > 0 ? transition.DurationSeconds : .35;
}
