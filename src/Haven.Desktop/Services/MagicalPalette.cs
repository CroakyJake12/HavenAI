using Avalonia.Media;

namespace Haven.Desktop.Services;

/// <summary>
/// Applies Haven's default magical AI palette to the existing app resource keys. The rest of
/// the application already uses these DynamicResource keys, so updating them here themes the
/// whole UI without broad descendant selectors or page-specific rewrites.
/// </summary>
public static class MagicalPalette
{
    private static bool _applied;

    public static void Apply()
    {
        if (_applied || Avalonia.Application.Current is not { } application) return;

        SetBrush("HavenBackgroundBrush", "#050B18");
        SetBrush("HavenElevatedBrush", "#B90A1425");
        SetBrush("HavenPanelBrush", "#A60C1728");
        SetBrush("HavenPanel2Brush", "#9C101B30");
        SetBrush("HavenPanel3Brush", "#B014223A");
        SetBrush("HavenPanelHoverBrush", "#3049FFE8");
        SetBrush("HavenTextBrush", "#F7FBFF");
        SetBrush("HavenTextSoftBrush", "#D6E8FF");
        SetBrush("HavenMutedBrush", "#A8B8D8");
        SetBrush("HavenMuted2Brush", "#7890B2");
        SetBrush("HavenAccentBrush", "#2BE7C8");
        SetBrush("HavenAccentInkBrush", "#041118");
        SetBrush("HavenAccentSoftBrush", "#44386CFF");
        SetBrush("HavenBlueBrush", "#69B8FF");
        SetBrush("HavenBlueSoftBrush", "#263A75FF");
        SetBrush("HavenDangerBrush", "#FF8AAE");
        SetBrush("HavenWarningBrush", "#FFE07A");
        SetBrush("HavenLineBrush", "#348CF7EA");
        SetBrush("HavenLineStrongBrush", "#5A8CF7EA");
        SetBrush("HavenNubBrush", "#FF5FA2");
        SetBrush("HavenMicaSurfaceBrush", "#261BE7C8");
        SetBrush("HavenMicaSidebarBrush", "#20101A2D");
        SetBrush("HavenAcrylicBrush", "#33101A2D");
        SetBrush("HavenButtonBrush", "#342BE7C8");
        SetBrush("HavenButtonHoverBrush", "#4649FFE8");
        SetBrush("HavenButtonPressedBrush", "#26386CFF");
        SetBrush("HavenFocusBrush", "#CCFF5FA2");

        // Compatibility aliases used by older views and generated UI fragments.
        SetBrush("PrimaryBrush", "#2BE7C8");
        SetBrush("StrokeBrush", "#5A8CF7EA");
        SetBrush("SurfaceCardBrush", "#B9101B30");
        SetBrush("TextPrimaryBrush", "#F7FBFF");

        application.Resources["HavenAcrylicTintColor"] = Color.Parse("#111A31");
        application.Resources["HavenAcrylicFallbackColor"] = Color.Parse("#F2111A31");
        _applied = true;
        return;

        void SetBrush(string key, string colour) =>
            application.Resources[key] = new SolidColorBrush(Color.Parse(colour));
    }
}
