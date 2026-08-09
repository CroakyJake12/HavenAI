using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Haven.Desktop.HavenUI.Components;

internal static class HavenControlThemeResolver
{
    internal static ControlTheme? For(Type baseControlType) =>
        Avalonia.Application.Current?.TryFindResource(baseControlType, out var value) == true
        && value is ControlTheme theme
            ? theme
            : null;
}
