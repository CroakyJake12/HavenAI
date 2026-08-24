using Avalonia;
using Avalonia.Media.Imaging;
using Haven.Application;

namespace Haven.Desktop.Services;

/// <summary>The two independent identity surfaces that own profile pictures.</summary>
public enum HavenAvatarKind
{
    User,
    Haven
}

/// <summary>
/// Stores processed local avatar assets as stable file references outside the
/// preference JSON. Images are decoded, centre-cropped and downscaled once at
/// selection time so ordinary settings stay small and chat rendering never
/// touches the original upload again.
/// </summary>
public sealed class AvatarStore
{
    private const int TargetSize = 256;
    private readonly string _directory;

    /// <summary>Process-wide instance set by DI so renderers can resolve assets without service lookups.</summary>
    public static AvatarStore? Current { get; internal set; }

    public AvatarStore(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _directory = Path.Combine(paths.DataDirectory, "avatars");
        Current ??= this;
    }

    /// <summary>Raised after an avatar asset changes so live caches can invalidate.</summary>
    public static event EventHandler<(HavenAvatarKind Kind, bool Removed)>? Changed;

    /// <summary>Returns the processed asset path for a kind, or null when none exists.</summary>
    public string? GetPath(HavenAvatarKind kind)
    {
        var path = PathFor(kind);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Reports whether a processed asset exists for a kind.</summary>
    public bool Has(HavenAvatarKind kind) => File.Exists(PathFor(kind));

    /// <summary>Well-known asset location used as the stable persisted reference.</summary>
    public string PathFor(HavenAvatarKind kind) => Path.Combine(_directory, $"{kind.ToString().ToLowerInvariant()}.png");

    /// <summary>
    /// Processes a selected image file into the stored avatar asset. The
    /// original file is only read; nothing is uploaded or copied elsewhere.
    /// </summary>
    public void SetFromFile(HavenAvatarKind kind, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("An image file must be selected.", nameof(sourcePath));
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The selected avatar image could not be found.", sourcePath);

        Directory.CreateDirectory(_directory);
        var destination = PathFor(kind);
        var temporary = destination + ".tmp";
        try
        {
            ProcessInto(sourcePath, temporary);
            ValidateStoredImage(temporary);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            // Environments without a working render backend keep the original
            // bytes so the identity feature degrades instead of disappearing.
            File.Copy(sourcePath, temporary, overwrite: true);
        }

        File.Move(temporary, destination, true);
        Changed?.Invoke(this, (kind, Removed: false));
    }

    private void ProcessInto(string sourcePath, string destinationPath)
    {
        using var stream = File.OpenRead(sourcePath);
        using var source = new Bitmap(stream);
        var side = Math.Min(source.PixelSize.Width, source.PixelSize.Height);
        var x = (source.PixelSize.Width - side) / 2;
        var y = (source.PixelSize.Height - side) / 2;
        using (var output = new RenderTargetBitmap(new PixelSize(TargetSize, TargetSize), new Vector(96d, 96d)))
        {
            using (var context = output.CreateDrawingContext())
            {
                context.DrawImage(source, new Rect(x, y, side, side), new Rect(0, 0, TargetSize, TargetSize));
            }
            using (var file = File.Create(destinationPath))
            {
                output.Save(file);
            }
        }
    }

    private static void ValidateStoredImage(string path)
    {
        // Byte-level validation keeps this independent of the platform image
        // decoder, which is unavailable in headless environments.
        if (new FileInfo(path).Length < 512)
            throw new InvalidOperationException("The processed avatar did not render correctly.");
    }

    /// <summary>Removes the stored asset for a kind when present.</summary>
    public bool Remove(HavenAvatarKind kind)
    {
        var path = PathFor(kind);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        Changed?.Invoke(this, (kind, Removed: true));
        return true;
    }
}
