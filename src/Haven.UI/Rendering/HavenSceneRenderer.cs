using Haven.UI.Components;
using HavenImageComponent = Haven.UI.Components.Image;

namespace Haven.UI;

public sealed class HavenSceneRenderer
{
    public IReadOnlyList<HavenDrawCommand> Render(HavenElement root)
    {
        var context = new HavenDrawingContext();
        RenderElement(root, context);
        return context.Commands;
    }

    private static void RenderElement(HavenElement element, HavenDrawingContext context)
    {
        if (!element.IsIncluded || element.GetValue(HavenProperties.Visibility) != HavenVisibility.Visible) return;
        var opacity = Math.Clamp(element.GetValue(HavenProperties.Opacity), 0d, 1d);
        var scale = element.GetValue(HavenProperties.Scale);
        var rotation = element.GetValue(HavenProperties.Rotation);
        var translateX = ResolvePixels(element.GetValue(HavenProperties.TranslationX));
        var translateY = ResolvePixels(element.GetValue(HavenProperties.TranslationY));
        var transformed = Math.Abs(scale - 1d) > .0001d || Math.Abs(rotation) > .0001d || Math.Abs(translateX) > .0001d || Math.Abs(translateY) > .0001d;
        if (transformed)
            context.Add(new HavenPushTransformCommand(element.Bounds, new HavenTransform(scale, scale, rotation, translateX, translateY), new HavenPoint(element.Bounds.X + element.Bounds.Width / 2d, element.Bounds.Y + element.Bounds.Height / 2d)));

        var radius = ResolvePixels(element.GetValue(HavenProperties.Radius).TopLeft);
        if (HavenEffects.TryResolveShadow(element.GetValue(HavenProperties.Shadow), out var shadow) && shadow is not null)
            context.Add(new HavenShadowCommand(element.Bounds, shadow with { Opacity = shadow.Opacity * opacity }, radius));
        var glow = element.GetValue(HavenProperties.Glow);
        if (!glow.Equals("None", StringComparison.OrdinalIgnoreCase)) context.Add(new HavenGlowCommand(element.Bounds, new HavenGlow(new HavenTokenBrush(glow), 18, opacity), radius));
        var background = element.GetValue(HavenProperties.Background);
        if (!background.Equals("Transparent", StringComparison.OrdinalIgnoreCase)) context.Add(new HavenFillRoundedRectCommand(element.Bounds, new HavenTokenBrush(background), radius, opacity));
        var borderWidth = ResolvePixels(element.GetValue(HavenProperties.BorderWidth));
        if (borderWidth > 0) context.Add(new HavenStrokeRoundedRectCommand(element.Bounds, new HavenPen(new HavenTokenBrush(element.GetValue(HavenProperties.BorderColor)), borderWidth), radius, opacity));

        var clipped = element.GetValue(HavenProperties.Clip)
            || element.GetValue(HavenProperties.Overflow) is HavenOverflow.Clip or HavenOverflow.Scroll;
        if (clipped) context.Add(new HavenPushClipCommand(element.Bounds));

        switch (element)
        {
            case Text text: DrawText(text.Content, element, context, opacity); break;
            case Button button when !string.IsNullOrWhiteSpace(button.Content): DrawText(button.Content, element, context, opacity); break;
            case Input input: DrawText(string.IsNullOrEmpty(input.Text) ? input.Placeholder : input.Text, element, context, string.IsNullOrEmpty(input.Text) ? opacity * .64 : opacity); break;
            case Select select: DrawText(select.SelectedItem ?? "Select", element, context, opacity); break;
            case Toggle toggle: DrawToggle(toggle, context, opacity); break;
            case Slider slider: DrawSlider(slider, context, opacity); break;
            case Progress progress: DrawProgress(progress, context, opacity); break;
            case Separator separator: DrawSeparator(separator, context, opacity); break;
            case HavenImageComponent image when !string.IsNullOrWhiteSpace(image.Source): context.Add(new HavenImageCommand(element.Bounds, new HavenImage(image.Source), MapImageLayout(image.Fit), opacity)); break;
            case Icon icon when !string.IsNullOrWhiteSpace(icon.Key): context.Add(new HavenIconCommand(element.Bounds, icon.Key, new HavenTokenBrush(element.GetValue(HavenProperties.Foreground)), opacity)); break;
        }
        foreach (var child in element.Children.OrderBy(child => child.GetValue(HavenProperties.ZIndex))) RenderElement(child, context);
        if (clipped) context.Add(new HavenPopClipCommand(element.Bounds));
        if (transformed) context.Add(new HavenPopTransformCommand(element.Bounds));
    }

    private static void DrawText(string value, HavenElement element, HavenDrawingContext context, double opacity)
    {
        if (string.IsNullOrEmpty(value)) return;
        var padding = element.GetValue(HavenProperties.Padding);
        var left = ResolvePixels(padding.Left); var top = ResolvePixels(padding.Top); var right = ResolvePixels(padding.Right); var bottom = ResolvePixels(padding.Bottom);
        var rect = new HavenRect(element.Bounds.X + left, element.Bounds.Y + top, Math.Max(0, element.Bounds.Width - left - right), Math.Max(0, element.Bounds.Height - top - bottom));
        context.Add(new HavenTextCommand(rect, new HavenTextLayout(value, element.GetValue(HavenProperties.FontFamily), element.GetValue(HavenProperties.FontSize), element.GetValue(HavenProperties.FontWeight), rect.Width), new HavenTokenBrush(element.GetValue(HavenProperties.Foreground)), opacity));
    }

    private static void DrawToggle(Toggle toggle, HavenDrawingContext context, double opacity)
    {
        var diameter = Math.Max(0, toggle.Bounds.Height - 6);
        var x = toggle.IsChecked ? toggle.Bounds.Right - diameter - 3 : toggle.Bounds.X + 3;
        context.Add(new HavenEllipseCommand(new HavenRect(x, toggle.Bounds.Y + 3, diameter, diameter), new HavenTokenBrush("TextOnAccent"), null, opacity));
    }

    private static void DrawSlider(Slider slider, HavenDrawingContext context, double opacity)
    {
        var trackHeight = ResolvePixels(SliderDefaults.TrackHeight);
        var track = new HavenRect(slider.Bounds.X, slider.Bounds.Y + (slider.Bounds.Height - trackHeight) / 2, slider.Bounds.Width, trackHeight);
        context.Add(new HavenFillRoundedRectCommand(track, new HavenTokenBrush("SurfaceRaised"), trackHeight / 2, opacity));
        var activeWidth = track.Width * slider.NormalizedValue;
        if (activeWidth > 0) context.Add(new HavenFillRoundedRectCommand(track with { Width = activeWidth }, new HavenTokenBrush("Accent"), trackHeight / 2, opacity));
        var thumb = ResolvePixels(SliderDefaults.ThumbSize);
        var centerX = track.X + track.Width * slider.NormalizedValue;
        context.Add(new HavenEllipseCommand(new HavenRect(centerX - thumb / 2, slider.Bounds.Y + (slider.Bounds.Height - thumb) / 2, thumb, thumb), new HavenTokenBrush("TextPrimary"), null, opacity));
    }

    private static void DrawProgress(Progress progress, HavenDrawingContext context, double opacity)
    {
        var active = progress.Bounds with { Width = progress.Bounds.Width * progress.NormalizedValue };
        if (active.Width > 0) context.Add(new HavenFillRoundedRectCommand(active, new HavenTokenBrush(progress.GetValue(HavenProperties.Foreground)), ResolvePixels(progress.GetValue(HavenProperties.Radius).TopLeft), opacity));
    }

    private static void DrawSeparator(Separator separator, HavenDrawingContext context, double opacity)
    {
        var start = new HavenPoint(separator.Bounds.X, separator.Bounds.Y);
        var end = separator.Orientation == SeparatorOrientation.Horizontal ? new HavenPoint(separator.Bounds.Right, separator.Bounds.Y) : new HavenPoint(separator.Bounds.X, separator.Bounds.Bottom);
        context.Add(new HavenLineCommand(start, end, new HavenPen(new HavenTokenBrush(separator.GetValue(HavenProperties.Background)), 1), opacity));
    }

    private static double ResolvePixels(HavenLength length) => length.Unit == HavenLengthUnit.Pixel ? Math.Max(0, length.Value) : 0;

    private static HavenImageLayout MapImageLayout(HavenImageFit fit) => fit switch
    {
        HavenImageFit.Cover => HavenImageLayout.Cover,
        HavenImageFit.Fill => HavenImageLayout.Fill,
        HavenImageFit.None => HavenImageLayout.None,
        _ => HavenImageLayout.Contain
    };
}
