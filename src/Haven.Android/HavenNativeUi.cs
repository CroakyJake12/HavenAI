using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Widget;

namespace Haven.Android;

internal sealed record HavenNativeGradient(Color Start, Color Middle, Color End)
{
    public GradientDrawable Create(float radius)
    {
        var drawable = new GradientDrawable(
            GradientDrawable.Orientation.LeftRight,
            [Start.ToArgb(), Middle.ToArgb(), End.ToArgb()]);
        drawable.SetCornerRadius(radius);
        return drawable;
    }
}

internal sealed record HavenNativeAccentPalette(
    HavenNativeGradient Primary,
    HavenNativeGradient Secondary,
    HavenNativeGradient Tertiary)
{
    internal static HavenNativeAccentPalette Launcher { get; } = new(
        new HavenNativeGradient(Color.Rgb(143, 74, 224), Color.Rgb(113, 58, 190), Color.Rgb(79, 37, 144)),
        new HavenNativeGradient(Color.Rgb(181, 116, 246), Color.Rgb(143, 78, 215), Color.Rgb(102, 50, 167)),
        new HavenNativeGradient(Color.Rgb(54, 28, 78), Color.Rgb(69, 35, 97), Color.Rgb(83, 43, 112)));

    internal static HavenNativeAccentPalette Negative { get; } = new(
        new HavenNativeGradient(Color.Rgb(255, 52, 61), Color.Rgb(255, 12, 24), Color.Rgb(230, 0, 11)),
        new HavenNativeGradient(Color.Rgb(255, 102, 109), Color.Rgb(245, 46, 55), Color.Rgb(205, 0, 10)),
        new HavenNativeGradient(Color.Rgb(76, 18, 25), Color.Rgb(93, 22, 29), Color.Rgb(112, 27, 35)));
}

/// <summary>Native-Android counterpart to HavenPrimaryButton.</summary>
internal class HavenNativeButton : Button
{
    private HavenNativeAccentPalette _palette;

    public HavenNativeButton(Context context, HavenNativeAccentPalette? palette = null) : base(context)
    {
        _palette = palette ?? HavenNativeAccentPalette.Launcher;
        SetAllCaps(false);
        SetTextColor(Color.White);
        Elevation = 8;
        AndroidTypography.Apply(this, NativeFontEmphasis.ExtraBold);
        ApplyAccentPalette(_palette);
    }

    public void ApplyAccentPalette(HavenNativeAccentPalette palette)
    {
        _palette = palette;
        Background = palette.Primary.Create(999f);
    }
}

internal sealed class HavenNativeNegativeButton : HavenNativeButton
{
    public HavenNativeNegativeButton(Context context) : base(context, HavenNativeAccentPalette.Negative) { }
}

/// <summary>Native checkbox hosted on the same live tertiary gradient tier.</summary>
internal sealed class HavenNativeCheckBox : CheckBox
{
    public HavenNativeCheckBox(Context context, HavenNativeAccentPalette? palette = null) : base(context)
    {
        var resolved = palette ?? HavenNativeAccentPalette.Launcher;
        Background = resolved.Tertiary.Create(28f);
        ButtonTintList = new ColorStateList(
            [new[] { global::Android.Resource.Attribute.StateChecked }, Array.Empty<int>()],
            [resolved.Primary.Middle.ToArgb(), Color.White.ToArgb()]);
        SetTextColor(Color.White);
        SetPadding(18, 8, 18, 8);
        AndroidTypography.Apply(this, NativeFontEmphasis.SemiBold);
    }
}

internal static class HavenNativeSurface
{
    internal static GradientDrawable Page(HavenNativeAccentPalette? palette = null)
    {
        var resolved = palette ?? HavenNativeAccentPalette.Launcher;
        return new GradientDrawable(
            GradientDrawable.Orientation.LeftRight,
            [Color.Rgb(7, 9, 14).ToArgb(), Color.Rgb(19, 14, 29).ToArgb(), resolved.Tertiary.Middle.ToArgb()]);
    }
}
