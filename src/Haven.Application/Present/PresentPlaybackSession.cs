using Haven.Core;

namespace Haven.Application;

public sealed record PresentPlaybackFrame(
    PresentSlide Slide,
    int SlideNumber,
    int SlideCount,
    string SpeakerNotes,
    TimeSpan Elapsed,
    int AnimationStep,
    IReadOnlyList<PresentAnimationCue> ActiveAnimations);

public sealed class PresentPlaybackSession
{
    private readonly PresentDocument _document;
    private readonly IReadOnlyList<PresentSlide> _slides;
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset _startedAt;
    private int _slideIndex;
    private int _animationStep;

    public PresentPlaybackSession(PresentDocument document, TimeProvider? timeProvider = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _document.Normalize();
        _slides = _document.Slides.Where(slide => !slide.Hidden).ToArray();
        if (_slides.Count == 0) throw new InvalidOperationException("A presentation needs at least one visible slide for playback.");
        _timeProvider = timeProvider ?? TimeProvider.System;
        _startedAt = _timeProvider.GetUtcNow();
    }

    public PresentDocument Document => _document;
    public int SlideIndex => _slideIndex;
    public int SlideCount => _slides.Count;
    public PresentSlide CurrentSlide => _slides[_slideIndex];
    public string SpeakerNotes => CurrentSlide.SpeakerNotes;
    public TimeSpan Elapsed => _timeProvider.GetUtcNow() - _startedAt;
    public int AnimationStep => _animationStep;
    public bool CanGoPrevious => _slideIndex > 0;
    public bool CanGoNext => _slideIndex < _slides.Count - 1;
    public IReadOnlyList<PresentSlide> Overview => _slides;

    public PresentPlaybackFrame Frame => new(
        CurrentSlide,
        _slideIndex + 1,
        _slides.Count,
        CurrentSlide.SpeakerNotes,
        Elapsed,
        _animationStep,
        CurrentSlide.Animations.Take(_animationStep).ToArray());

    public bool Next()
    {
        if (!CanGoNext) return false;
        _slideIndex++;
        _animationStep = 0;
        return true;
    }

    public bool Previous()
    {
        if (!CanGoPrevious) return false;
        _slideIndex--;
        _animationStep = 0;
        return true;
    }

    public bool GoTo(int slideIndex)
    {
        if (slideIndex < 0 || slideIndex >= _slides.Count || slideIndex == _slideIndex) return false;
        _slideIndex = slideIndex;
        _animationStep = 0;
        return true;
    }

    public bool Advance()
    {
        if (_animationStep < CurrentSlide.Animations.Count)
        {
            _animationStep++;
            return true;
        }
        return Next();
    }

    public void Restart()
    {
        _slideIndex = 0;
        _animationStep = 0;
        _startedAt = _timeProvider.GetUtcNow();
    }
}
