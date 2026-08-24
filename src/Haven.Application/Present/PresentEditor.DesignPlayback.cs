using Haven.Core;

namespace Haven.Application;

public sealed partial class PresentEditor
{
    public bool SetSlideSizePreset(PresentSlideSizePreset preset)
    {
        if (Document.SlideSize.Preset == preset) return false;
        Mutate(() =>
        {
            Document.SlideSize.Preset = preset;
            Document.SlideSize.Normalize();
        });
        return true;
    }

    public bool ApplyThemePreset(string preset)
    {
        var theme = CreateThemePreset(preset);
        if (ThemeEquals(Document.Theme, theme)) return false;
        Mutate(() => Document.Theme = theme);
        return true;
    }

    public bool SetSlideLayout(Guid slideId, Guid layoutId)
    {
        var slide = RequireSlide(slideId);
        var layout = Document.Layouts.FirstOrDefault(value => value.Id == layoutId);
        if (layout is null || slide.LayoutId == layout.Id) return false;
        Mutate(() =>
        {
            slide.LayoutId = layout.Id;
            var bodyPlaceholder = layout.Placeholders.FirstOrDefault(value => value.Kind == PresentPlaceholderKind.Body);
            var body = slide.Elements.FirstOrDefault(value => value.Kind == PresentElementKind.Text && string.Equals(value.Role, PresentElementRoles.Body, StringComparison.OrdinalIgnoreCase));
            if (bodyPlaceholder is not null && body is not null && !body.Locked)
            {
                body.X = bodyPlaceholder.X;
                body.Y = bodyPlaceholder.Y;
                body.Width = bodyPlaceholder.Width;
                body.Height = bodyPlaceholder.Height;
            }
        });
        return true;
    }

    public bool SetSlideBackgroundColor(Guid slideId, string? color)
    {
        var slide = RequireSlide(slideId);
        var next = string.IsNullOrWhiteSpace(color) ? string.Empty : color.Trim();
        var nextKind = string.IsNullOrEmpty(next) ? PresentBackgroundKind.Theme : PresentBackgroundKind.Solid;
        if (slide.Background.Kind == nextKind && string.Equals(slide.Background.Color, next, StringComparison.OrdinalIgnoreCase)) return false;
        Mutate(() =>
        {
            slide.Background.Kind = nextKind;
            slide.Background.Color = next;
            if (nextKind != PresentBackgroundKind.Image) slide.Background.AssetId = string.Empty;
        });
        return true;
    }

    public bool SetSlideTransition(
        Guid slideId,
        PresentTransitionKind kind,
        double durationSeconds = .35,
        PresentEasingKind easing = PresentEasingKind.EaseInOut,
        PresentMotionDirection direction = PresentMotionDirection.None)
    {
        var slide = RequireSlide(slideId);
        var duration = kind == PresentTransitionKind.None ? 0 : Math.Clamp(double.IsFinite(durationSeconds) ? durationSeconds : .35, .05, 30);
        if (slide.Transition.Kind == kind && Math.Abs(slide.Transition.DurationSeconds - duration) < .001 && slide.Transition.Easing == easing && slide.Transition.Direction == direction) return false;
        Mutate(() =>
        {
            slide.Transition.Kind = kind;
            slide.Transition.DurationSeconds = duration;
            slide.Transition.Easing = easing;
            slide.Transition.Direction = direction;
        });
        return true;
    }

    public IReadOnlyList<Guid> AddAnimationToSelection(
        PresentAnimationEffect effect,
        PresentAnimationTrigger trigger = PresentAnimationTrigger.OnClick,
        double durationSeconds = .35,
        PresentMotionDirection direction = PresentMotionDirection.None)
    {
        var selected = EditableSelection().Where(value => value.Kind != PresentElementKind.Group).ToArray();
        if (selected.Length == 0) return Array.Empty<Guid>();
        var ids = new List<Guid>();
        Mutate(() =>
        {
            var slide = SelectedSlide;
            foreach (var element in selected)
            {
                var cue = new PresentAnimationCue
                {
                    TargetElementId = element.Id,
                    Effect = effect,
                    Trigger = trigger,
                    DurationSeconds = Math.Clamp(double.IsFinite(durationSeconds) ? durationSeconds : .35, .01, 120),
                    Direction = direction,
                    Order = slide.Animations.Count
                };
                slide.Animations.Add(cue);
                ids.Add(cue.Id);
            }
        });
        return ids;
    }

    public bool RemoveAnimationsFromSelection()
    {
        var selected = _selectedElementIds.ToHashSet();
        if (selected.Count == 0 || SelectedSlide.Animations.All(cue => !selected.Contains(cue.TargetElementId))) return false;
        Mutate(() => SelectedSlide.Animations.RemoveAll(cue => selected.Contains(cue.TargetElementId)));
        return true;
    }

    public bool SetAnimationTriggerForSelection(PresentAnimationTrigger trigger)
    {
        var selected = _selectedElementIds.ToHashSet();
        var cues = SelectedSlide.Animations.Where(cue => selected.Contains(cue.TargetElementId)).ToArray();
        if (cues.Length == 0 || cues.All(cue => cue.Trigger == trigger)) return false;
        Mutate(() => { foreach (var cue in cues) cue.Trigger = trigger; });
        return true;
    }

    public bool SetSelectedMediaPlayback(bool autoPlay, bool loop, double startSeconds = 0, double? endSeconds = null)
    {
        var selected = EditableSelection().Where(element => element.Kind == PresentElementKind.Media).ToArray();
        if (selected.Length == 0) return false;
        var start = double.IsFinite(startSeconds) ? Math.Max(0, startSeconds) : 0;
        double? end = endSeconds is { } finiteEnd && double.IsFinite(finiteEnd) ? Math.Max(start, finiteEnd) : null;
        if (selected.All(element => element.Media.AutoPlay == autoPlay && element.Media.Loop == loop && Math.Abs(element.Media.StartSeconds - start) < .001 && element.Media.EndSeconds == end)) return false;
        Mutate(() =>
        {
            foreach (var element in selected)
            {
                element.Media.AutoPlay = autoPlay;
                element.Media.Loop = loop;
                element.Media.StartSeconds = start;
                element.Media.EndSeconds = end;
            }
        });
        return true;
    }

    public bool SetSelectedAlternativeText(string? alternativeText)
    {
        var selected = EditableSelection().Where(element => element.Kind is PresentElementKind.Image or PresentElementKind.Media or PresentElementKind.Shape).ToArray();
        if (selected.Length == 0) return false;
        var next = alternativeText?.Trim() ?? string.Empty;
        if (selected.All(element => string.Equals(element.AlternativeText, next, StringComparison.Ordinal))) return false;
        Mutate(() => { foreach (var element in selected) element.AlternativeText = next; });
        return true;
    }

    public bool SetSlideHidden(Guid slideId, bool hidden)
    {
        var slide = RequireSlide(slideId);
        if (slide.Hidden == hidden) return false;
        Mutate(() => slide.Hidden = hidden);
        return true;
    }

    private static PresentTheme CreateThemePreset(string? preset)
    {
        return (preset ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "midnight" => new PresentTheme
            {
                Name = "Midnight", HeadingFontFamily = "Aptos Display", BodyFontFamily = "Aptos",
                Colors = new PresentThemeColors { Background = "#111827", Foreground = "#F9FAFB", Accent1 = "#60A5FA", Accent2 = "#A78BFA", Accent3 = "#34D399", Accent4 = "#F472B6", Accent5 = "#FBBF24", Accent6 = "#22D3EE" }
            },
            "paper" => new PresentTheme
            {
                Name = "Paper", HeadingFontFamily = "Georgia", BodyFontFamily = "Georgia",
                Colors = new PresentThemeColors { Background = "#FBF7EF", Foreground = "#28231D", Accent1 = "#A14832", Accent2 = "#416B8A", Accent3 = "#5D7D56", Accent4 = "#765A83", Accent5 = "#A9762B", Accent6 = "#3F7D78" }
            },
            "aurora" => new PresentTheme
            {
                Name = "Aurora", HeadingFontFamily = "Montserrat", BodyFontFamily = "Montserrat",
                Colors = new PresentThemeColors { Background = "#F7FBFF", Foreground = "#14213D", Accent1 = "#5B5BD6", Accent2 = "#2F9EAA", Accent3 = "#46A758", Accent4 = "#AB4ABA", Accent5 = "#E5A000", Accent6 = "#E5484D" }
            },
            _ => new PresentTheme { Name = "Haven" }
        };
    }

    private static bool ThemeEquals(PresentTheme left, PresentTheme right)
    {
        return string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && string.Equals(left.HeadingFontFamily, right.HeadingFontFamily, StringComparison.Ordinal)
            && string.Equals(left.BodyFontFamily, right.BodyFontFamily, StringComparison.Ordinal)
            && string.Equals(left.Colors.Background, right.Colors.Background, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Colors.Foreground, right.Colors.Foreground, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Colors.Accent1, right.Colors.Accent1, StringComparison.OrdinalIgnoreCase);
    }
}
