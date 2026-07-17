using Avalonia.Media;

namespace Haven.Desktop.Controls;

internal static class Transform
{
    public static ITransform Identity { get; } = new ScaleTransform(1, 1);
}
