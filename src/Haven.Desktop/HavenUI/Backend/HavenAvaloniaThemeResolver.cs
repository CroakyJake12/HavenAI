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
            "Accent" => "HavenAccentPrimaryBrush", "AccentHover" => "HavenAccentPrimaryHoverBrush", "AccentSecondary" => "HavenAccentSecondaryBrush", "AccentSecondaryHover" => "HavenAccentSecondaryHoverBrush", "AccentMuted" => "HavenAccentTertiaryBrush", "AccentTertiaryHover" => "HavenAccentTertiaryHoverBrush", "AccentGlow" => "HavenAccentPrimaryBrush", "AccentSecondaryGlow" => "HavenAccentSecondaryBrush", "AccentTertiaryGlow" => "HavenAccentTertiaryBrush", "Surface" => "HavenPanelBrush", "SurfaceRaised" => "HavenPanel2Brush", "TextPrimary" => "HavenTextPrimaryBrush", "TextSecondary" => "HavenTextSecondaryBrush", "TextOnAccent" => "HavenAccentForegroundBrush", "Border" => "HavenBorderSubtleBrush", "Shadow" => "HavenShadowBrush", "Danger" => "HavenAttentionBrush", "DangerHover" => "HavenAttentionBrush", "TextOnDanger" => "HavenTextPrimaryBrush", "DangerGlow" => "HavenAttentionBrush", "Transparent" or "None" => null, _ => token
        };
        if (key is null) return Brushes.Transparent;
        if (Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush) return brush;
        throw new InvalidOperationException($"Haven semantic brush '{token}' resolved to missing Avalonia resource '{key}'.");
    }

    internal static Color ResolveColor(string token, double opacity = 1d)
    {
        if (Resolve(token) is not ISolidColorBrush solid)
            throw new InvalidOperationException($"Haven semantic brush '{token}' must resolve to a solid colour for this effect.");
        var alpha = (byte)Math.Clamp(Math.Round(solid.Color.A * Math.Clamp(opacity, 0d, 1d)), 0d, 255d);
        return solid.Color with { A = alpha };
    }
}
