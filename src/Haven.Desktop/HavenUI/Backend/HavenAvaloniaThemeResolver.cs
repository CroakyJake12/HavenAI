using Avalonia.Media;

namespace Haven.Desktop.HavenUI.Backend;

internal static class HavenAvaloniaThemeResolver
{
    internal static IBrush Resolve(string token)
    {
        var key = token switch
        {
            "Accent" => "HavenAccentPrimaryBrush", "AccentHover" => "HavenAccentPrimaryHoverBrush", "AccentSecondary" => "HavenAccentSecondaryBrush", "AccentSecondaryHover" => "HavenAccentSecondaryHoverBrush", "AccentMuted" => "HavenAccentTertiaryBrush", "AccentTertiaryHover" => "HavenAccentTertiaryHoverBrush", "AccentGlow" => "HavenAccentPrimaryBrush", "AccentSecondaryGlow" => "HavenAccentSecondaryBrush", "AccentTertiaryGlow" => "HavenAccentTertiaryBrush", "Surface" => "HavenPanelBrush", "SurfaceRaised" => "HavenPanel2Brush", "TextPrimary" => "HavenTextPrimaryBrush", "TextSecondary" => "HavenTextSecondaryBrush", "TextOnAccent" => "HavenAccentForegroundBrush", "Border" => "HavenBorderSubtleBrush", "Danger" => "HavenAttentionBrush", "DangerHover" => "HavenAttentionBrush", "TextOnDanger" => "HavenTextPrimaryBrush", "DangerGlow" => "HavenAttentionBrush", "Transparent" or "None" => null, _ => token
        };
        if (key is null) return Brushes.Transparent;
        if (Avalonia.Application.Current?.Resources[key] is IBrush brush) return brush;
        throw new InvalidOperationException($"Haven semantic brush '{token}' resolved to missing Avalonia resource '{key}'.");
    }
}
