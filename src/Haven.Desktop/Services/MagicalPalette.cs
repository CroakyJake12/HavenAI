/*
 * FILE DOCUMENTATION
 * Where: src/Haven.OldHaven/Services/MagicalPalette.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns MagicalPalette. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Styling;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Tokens;

namespace Haven.Desktop.Services;

/// <summary>
/// Compatibility entry point for older startup code. The canonical HavenUI
/// catalogue now owns all palette values, including the required three live
/// gradient accent tiers.
/// </summary>
public static class MagicalPalette
{
    /// <summary>
    /// Performs the apply step owned by this component.
    /// </summary>
    public static void Apply()
    {
        if (Avalonia.Application.Current is not { } application) return;
        var appearance = application.RequestedThemeVariant == ThemeVariant.Light
            ? HavenUiAppearance.Bright
            : HavenUiAppearance.SuperDark;
        HavenUiResourceApplier.Apply(SurfacePaletteCatalog.For(HavenSurface.Home, appearance));
    }
}
