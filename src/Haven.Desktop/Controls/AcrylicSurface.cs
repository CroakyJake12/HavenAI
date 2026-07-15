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
    public static readonly StyledProperty<Color> TintColorProperty =
        AvaloniaProperty.Register<AcrylicSurface, Color>(nameof(TintColor), Color.Parse("#182234"));

    public static readonly StyledProperty<Color> FallbackColorProperty =
        AvaloniaProperty.Register<AcrylicSurface, Color>(nameof(FallbackColor), Color.Parse("#F2182234"));

    public static readonly StyledProperty<double> TintOpacityProperty =
        AvaloniaProperty.Register<AcrylicSurface, double>(nameof(TintOpacity), 0.78d);

    public static readonly StyledProperty<double> MaterialOpacityProperty =
        AvaloniaProperty.Register<AcrylicSurface, double>(nameof(MaterialOpacity), 0.62d);

    public Color TintColor { get => GetValue(TintColorProperty); set => SetValue(TintColorProperty, value); }
    public Color FallbackColor { get => GetValue(FallbackColorProperty); set => SetValue(FallbackColorProperty, value); }
    public double TintOpacity { get => GetValue(TintOpacityProperty); set => SetValue(TintOpacityProperty, value); }
    public double MaterialOpacity { get => GetValue(MaterialOpacityProperty); set => SetValue(MaterialOpacityProperty, value); }
}
