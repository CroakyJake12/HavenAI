using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Haven.Desktop.HavenUI.Backend;

/// <summary>Backend-owned image decoding. Haven.UI sees only semantic source strings.</summary>
public interface IHavenAvaloniaImageResolver
{
    bool TryResolve(string source, out IImage? image);
}

/// <summary>
/// Resolves packaged Avalonia assets only. File, network and data sources must
/// be supplied through an explicit capability-aware resolver by the host.
/// </summary>
public sealed class HavenAvaloniaImageResolver : IHavenAvaloniaImageResolver
{
    private readonly Dictionary<string, IImage> _cache = new(StringComparer.Ordinal);

    public bool TryResolve(string source, out IImage? image)
    {
        image = null;
        if (string.IsNullOrWhiteSpace(source)) return false;
        if (_cache.TryGetValue(source, out var cached)) { image = cached; return true; }
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("avares" or "resm")) return false;
        try
        {
            using var stream = AssetLoader.Open(uri);
            image = new Bitmap(stream);
            _cache[source] = image;
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }
}
