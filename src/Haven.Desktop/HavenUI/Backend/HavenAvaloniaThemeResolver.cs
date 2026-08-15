using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Haven.Desktop.HavenUI.Backend;

internal static class HavenAvaloniaThemeResolver
{
    internal static IBrush Resolve(string token)
    {
        var key = token switch
        {
            "Accent" => "HavenAccentPrimaryBrush", "AccentHover" => "HavenAccentPrimaryHoverBrush", "AccentSecondary" => "HavenAccentSecondaryBrush", "AccentSecondaryHover" => "HavenAccentSecondaryHoverBrush", "AccentMuted" => "HavenAccentTertiaryBrush", "AccentTertiaryHover" => "HavenAccentTertiaryHoverBrush", "AccentGlow" => "HavenAccentPrimaryBrush", "AccentSecondaryGlow" => "HavenAccentSecondaryBrush", "AccentTertiaryGlow" => "HavenAccentTertiaryBrush", "Surface" => "HavenPanelBrush", "SurfaceRaised" => "HavenPanel2Brush", "TextPrimary" => "HavenTextPrimaryBrush", "TextSecondary" => "HavenTextSecondaryBrush", "TextOnAccent" => "HavenAccentForegroundBrush", "ButtonTextPrimary" => "HavenButtonTextPrimaryBrush", "ButtonTextSecondary" => "HavenButtonTextSecondaryBrush", "Border" => "HavenBorderSubtleBrush", "Shadow" => "HavenShadowBrush", "Danger" => "HavenAttentionBrush", "DangerHover" => "HavenAttentionBrush", "TextOnDanger" => "HavenTextPrimaryBrush", "DangerGlow" => "HavenAttentionBrush", "Transparent" or "None" => null, _ => token
        };
        if (key is null) return Brushes.Transparent;
        if (Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush) return brush;
        throw new InvalidOperationException($"Haven semantic brush '{token}' resolved to missing Avalonia resource '{key}'.");
    }

    internal static Color ResolveColor(string token, double opacity = 1d)
    {
        var color = EffectColor(Resolve(token), token);
        var alpha = (byte)Math.Clamp(Math.Round(color.A * Math.Clamp(opacity, 0d, 1d)), 0d, 255d);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    internal static Color EffectColor(IBrush brush, string description = "Haven effect")
    {
        if (brush is ISolidColorBrush solid) return solid.Color;
        if (brush is IGradientBrush gradient)
        {
            var stops = gradient.GradientStops.OrderBy(stop => Math.Abs(stop.Offset - .5d)).ToArray();
            if (stops.Length > 0) return stops[0].Color;
        }
        throw new InvalidOperationException($"{description} must resolve to a solid or gradient colour brush for this effect.");
    }
}
