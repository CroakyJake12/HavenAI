using Avalonia.Controls;

namespace Haven.Desktop;

/// <summary>
/// Compile-time compatibility for the desktop-only lifetime branch in App.axaml.cs.
/// Android uses ISingleViewApplicationLifetime and never instantiates this type.
/// </summary>
internal sealed class MainWindow : Window
{
}
