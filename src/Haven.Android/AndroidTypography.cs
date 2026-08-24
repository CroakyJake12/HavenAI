using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace Haven.Android;

/// <summary>
/// Applies Haven's bundled Montserrat typography to UI drawn by Haven's native
/// Android activities. Android-owned surfaces such as the document picker and
/// toasts intentionally retain the user's system typography.
/// </summary>
internal static class AndroidTypography
{
    private const string MediumAsset = "fonts/Montserrat-Medium.ttf";
    private const string SemiBoldAsset = "fonts/Montserrat-SemiBold.ttf";
    private const string ExtraBoldAsset = "fonts/Montserrat-ExtraBold.ttf";

    private static Typeface? _medium;
    private static Typeface? _semiBold;
    private static Typeface? _extraBold;

    public static void ApplyTree(View? view)
    {
        if (view is null) return;

        if (view is TextView text)
        {
            var emphasis = text.Typeface?.IsBold == true
                ? NativeFontEmphasis.ExtraBold
                : text is Button
                    ? NativeFontEmphasis.SemiBold
                    : NativeFontEmphasis.Medium;
            Apply(text, emphasis);
        }

        if (view is not ViewGroup group) return;
        for (var index = 0; index < group.ChildCount; index++)
            ApplyTree(group.GetChildAt(index));
    }

    public static void Apply(TextView text, NativeFontEmphasis emphasis = NativeFontEmphasis.Medium)
    {
        ArgumentNullException.ThrowIfNull(text);
        text.Typeface = emphasis switch
        {
            NativeFontEmphasis.ExtraBold => _extraBold ??= Load(ExtraBoldAsset),
            NativeFontEmphasis.SemiBold => _semiBold ??= Load(SemiBoldAsset),
            _ => _medium ??= Load(MediumAsset)
        };
    }

    private static Typeface Load(string assetPath)
        => Typeface.CreateFromAsset(global::Android.App.Application.Context.Assets, assetPath)
           ?? throw new InvalidOperationException($"Bundled Haven font asset '{assetPath}' could not be loaded.");
}

internal enum NativeFontEmphasis
{
    Medium,
    SemiBold,
    ExtraBold
}
