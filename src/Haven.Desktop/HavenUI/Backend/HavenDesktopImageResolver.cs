using Avalonia.Media;
using Haven.Desktop.Services;

namespace Haven.Desktop.HavenUI.Backend;

/// <summary>
/// Desktop image resolver: packaged application assets plus the local
/// capability-aware <c>avatar://</c> scheme backed by <see cref="AvatarStore"/>.
/// File, network and data sources remain unavailable to scene markup beyond
/// this explicit host-supplied capability.
/// </summary>
public sealed class HavenDesktopImageResolver : IHavenAvaloniaImageResolver
{
    public const string UserAvatarSource = "avatar://user";
    public const string HavenAvatarSource = "avatar://haven";

    private readonly IHavenAvaloniaImageResolver _packaged = new HavenAvaloniaImageResolver();
    private readonly Dictionary<string, IImage> _avatarCache = new(StringComparer.Ordinal);

    public HavenDesktopImageResolver()
    {
        AvatarStore.Changed += OnAvatarChanged;
    }

    public bool TryResolve(string source, out IImage? image)
    {
        image = null;
        if (string.IsNullOrWhiteSpace(source)) return false;
        if (!source.StartsWith("avatar://", StringComparison.OrdinalIgnoreCase))
            return _packaged.TryResolve(source, out image);

        if (_avatarCache.TryGetValue(source, out var cached)) { image = cached; return true; }
        var kind = source.EndsWith("user", StringComparison.OrdinalIgnoreCase) ? HavenAvatarKind.User : HavenAvatarKind.Haven;
        var path = AvatarStore.Current?.GetPath(kind);
        if (path is null) return false;
        try
        {
            using var stream = File.OpenRead(path);
            image = new Avalonia.Media.Imaging.Bitmap(stream);
            _avatarCache[source] = image!;
            return true;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            return false;
        }
    }

    private void OnAvatarChanged(object? sender, (HavenAvatarKind Kind, bool Removed) change)
    {
        var source = change.Kind == HavenAvatarKind.User ? UserAvatarSource : HavenAvatarSource;
        _avatarCache.Remove(source);
    }
}
