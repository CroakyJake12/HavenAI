/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/AcrylicSurface.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns AcrylicSurface. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Haven.Desktop.Controls;

/// <summary>
/// Reusable live backdrop surface for flyouts, menus and elevated panels.
/// The control theme supplies an opaque fallback when compositor acrylic is
/// unavailable, so consumers do not need platform-specific XAML.
/// </summary>
public sealed class AcrylicSurface : ContentControl
{
    /// <summary>
    /// Stores tint color property locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly StyledProperty<Color> TintColorProperty =
        AvaloniaProperty.Register<AcrylicSurface, Color>(nameof(TintColor), Color.Parse("#182234"));

    /// <summary>
    /// Stores fallback color property locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly StyledProperty<Color> FallbackColorProperty =
        AvaloniaProperty.Register<AcrylicSurface, Color>(nameof(FallbackColor), Color.Parse("#F2182234"));

    /// <summary>
    /// Stores tint opacity property locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly StyledProperty<double> TintOpacityProperty =
        AvaloniaProperty.Register<AcrylicSurface, double>(nameof(TintOpacity), 0.78d);

    /// <summary>
    /// Stores material opacity property locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly StyledProperty<double> MaterialOpacityProperty =
        AvaloniaProperty.Register<AcrylicSurface, double>(nameof(MaterialOpacity), 0.62d);

    /// <summary>
    /// Gets or updates tint color, the bindable or domain state represented by this property.
    /// </summary>
    public Color TintColor { get => GetValue(TintColorProperty); set => SetValue(TintColorProperty, value); }
    /// <summary>
    /// Gets or updates fallback color, the bindable or domain state represented by this property.
    /// </summary>
    public Color FallbackColor { get => GetValue(FallbackColorProperty); set => SetValue(FallbackColorProperty, value); }
    /// <summary>
    /// Gets or updates tint opacity, the bindable or domain state represented by this property.
    /// </summary>
    public double TintOpacity { get => GetValue(TintOpacityProperty); set => SetValue(TintOpacityProperty, value); }
    /// <summary>
    /// Gets or updates material opacity, the bindable or domain state represented by this property.
    /// </summary>
    public double MaterialOpacity { get => GetValue(MaterialOpacityProperty); set => SetValue(MaterialOpacityProperty, value); }
}
