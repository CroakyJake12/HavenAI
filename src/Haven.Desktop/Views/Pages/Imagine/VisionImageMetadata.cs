using SkiaSharp;

namespace Haven.Desktop.Views.Pages.Imagine;

internal readonly record struct VisionImageInfo(int Width, int Height);

/// <summary>Reads intrinsic image dimensions without decoding the full raster.</summary>
internal static class VisionImageMetadata
{
    internal static bool TryRead(string? path, out VisionImageInfo imageInfo)
    {
        imageInfo = default;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0) return false;
            imageInfo = new VisionImageInfo(codec.Info.Width, codec.Info.Height);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
