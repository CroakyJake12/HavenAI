/*
 * FILE DOCUMENTATION
 * Where: src/Haven.OldHaven/Services/MagicalPalette.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns MagicalPalette. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Media;

namespace Haven.Desktop.Services;

/// <summary>
/// Applies Haven's default magical AI palette to the existing app resource keys. The rest of
/// the application already uses these DynamicResource keys, so updating them here themes the
/// whole UI without broad descendant selectors or page-specific rewrites.
/// </summary>
public static class MagicalPalette
{
    /// <summary>
    /// Stores applied locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static bool _applied;

    /// <summary>
    /// Performs the apply step owned by this component.
    /// </summary>
    public static void Apply()
    {
        if (_applied || Avalonia.Application.Current is not { } application) return;

        SetBrush(application, "HavenBackgroundBrush", "#050B18");
        SetBrush(application, "HavenElevatedBrush", "#B90A1425");
        SetBrush(application, "HavenPanelBrush", "#A60C1728");
        SetBrush(application, "HavenPanel2Brush", "#9C101B30");
        SetBrush(application, "HavenPanel3Brush", "#B014223A");
        SetBrush(application, "HavenPanelHoverBrush", "#3049FFE8");
        SetBrush(application, "HavenTextBrush", "#F7FBFF");
        SetBrush(application, "HavenTextSoftBrush", "#D6E8FF");
        SetBrush(application, "HavenMutedBrush", "#A8B8D8");
        SetBrush(application, "HavenMuted2Brush", "#7890B2");
        SetBrush(application, "HavenAccentBrush", "#2BE7C8");
        SetBrush(application, "HavenAccentInkBrush", "#041118");
        SetBrush(application, "HavenAccentSoftBrush", "#44386CFF");
        SetBrush(application, "HavenBlueBrush", "#69B8FF");
        SetBrush(application, "HavenBlueSoftBrush", "#263A75FF");
        SetBrush(application, "HavenDangerBrush", "#FF8AAE");
        SetBrush(application, "HavenWarningBrush", "#FFE07A");
        SetBrush(application, "HavenLineBrush", "#348CF7EA");
        SetBrush(application, "HavenLineStrongBrush", "#5A8CF7EA");
        SetBrush(application, "HavenNubBrush", "#FF5FA2");
        SetBrush(application, "HavenMicaSurfaceBrush", "#261BE7C8");
        SetBrush(application, "HavenMicaSidebarBrush", "#20101A2D");
        SetBrush(application, "HavenAcrylicBrush", "#33101A2D");
        SetBrush(application, "HavenButtonBrush", "#342BE7C8");
        SetBrush(application, "HavenButtonHoverBrush", "#4649FFE8");
        SetBrush(application, "HavenButtonPressedBrush", "#26386CFF");
        SetBrush(application, "HavenFocusBrush", "#CCFF5FA2");

        // Compatibility aliases used by older views and generated UI fragments.
        SetBrush(application, "PrimaryBrush", "#2BE7C8");
        SetBrush(application, "StrokeBrush", "#5A8CF7EA");
        SetBrush(application, "SurfaceCardBrush", "#B9101B30");
        SetBrush(application, "TextPrimaryBrush", "#F7FBFF");

        application.Resources["HavenAcrylicTintColor"] = Color.Parse("#111A31");
        application.Resources["HavenAcrylicFallbackColor"] = Color.Parse("#F2111A31");
        _applied = true;
    }

    /// <summary>
    /// Performs the set brush step owned by this component.
    /// </summary>
    private static void SetBrush(Avalonia.Application application, string key, string colour) =>
        application.Resources[key] = new SolidColorBrush(Color.Parse(colour));
}
