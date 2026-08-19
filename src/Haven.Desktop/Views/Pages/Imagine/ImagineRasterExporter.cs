using System.Globalization;
using Haven.Core;
using SkiaSharp;

namespace Haven.Desktop.Views.Pages.Imagine;

/// <summary>Rasterises the committed Imagine image model without selection chrome or semantic overlays.</summary>
internal static class ImagineRasterExporter
{
    private const int MaxDimension = 16384;
    private const long MaxPixels = 100_000_000;

    public static async Task<string> ExportAsync(ImagineProject project, string destinationPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("An image export path is required.", nameof(destinationPath));
        var width = checked((int)Math.Round(project.CanvasWidth));
        var height = checked((int)Math.Round(project.CanvasHeight));
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
            throw new InvalidOperationException($"This {width}×{height} canvas is too large for the current raster exporter.");

        var destination = NormalizeDestination(destinationPath, out var format, out var quality);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        cancellationToken.ThrowIfCancellationRequested();
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Imagine could not allocate the raster export surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        foreach (var item in project.Objects.Where(item => item.IsVisible).OrderBy(item => item.ZIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DrawObject(canvas, project, item);
        }
        canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(format, quality) ?? throw new InvalidOperationException("Imagine could not encode the raster export.");
        await using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        data.SaveTo(stream);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
        return destination;
    }

    private static void DrawObject(SKCanvas canvas, ImagineProject project, ImagineEditableObject item)
    {
        var transform = item.Transform;
        var centerX = (float)(transform.X + transform.Width / 2);
        var centerY = (float)(transform.Y + transform.Height / 2);
        var width = (float)transform.Width;
        var height = (float)transform.Height;
        canvas.Save();
        canvas.Translate(centerX, centerY);
        if (Math.Abs(transform.RotationDegrees) > .01) canvas.RotateDegrees((float)transform.RotationDegrees);
        var rect = new SKRect(-width / 2, -height / 2, width / 2, height / 2);
        switch (item.Kind)
        {
            case ImagineObjectKind.Image: DrawImage(canvas, project, item, rect); break;
            case ImagineObjectKind.Ellipse:
                using (var paint = Paint(item.Fill)) canvas.DrawOval(rect, paint);
                break;
            case ImagineObjectKind.Text: DrawText(canvas, item, rect); break;
            default:
                using (var paint = Paint(item.Fill)) canvas.DrawRoundRect(rect, 8, 8, paint);
                break;
        }
        canvas.Restore();
    }

    private static void DrawImage(SKCanvas canvas, ImagineProject project, ImagineEditableObject item, SKRect destination)
    {
        if (item.AssetId is not Guid assetId || project.Assets.FirstOrDefault(asset => asset.Id == assetId) is not { } asset) return;
        if (!File.Exists(asset.ManagedPath)) throw new FileNotFoundException($"The imported image '{asset.Name}' is missing from Imagine's managed media.", asset.ManagedPath);
        using var bitmap = SKBitmap.Decode(asset.ManagedPath) ?? throw new InvalidDataException($"Imagine could not decode '{asset.Name}' for export.");
        if (bitmap.Width <= 0 || bitmap.Height <= 0) return;
        var scale = Math.Min(destination.Width / bitmap.Width, destination.Height / bitmap.Height);
        var drawWidth = bitmap.Width * scale;
        var drawHeight = bitmap.Height * scale;
        var fitted = new SKRect(-drawWidth / 2, -drawHeight / 2, drawWidth / 2, drawHeight / 2);
        canvas.DrawBitmap(bitmap, fitted);
    }

    private static void DrawText(SKCanvas canvas, ImagineEditableObject item, SKRect rect)
    {
        using var paint = Paint(item.Fill);
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var font = new SKFont(typeface, 26);
        var baseline = rect.Top - font.Metrics.Ascent;
        canvas.DrawText(item.Text ?? string.Empty, rect.Left, baseline, SKTextAlign.Left, font, paint);
    }

    private static SKPaint Paint(string value) => new() { Color = ParseColor(value), IsAntialias = true, Style = SKPaintStyle.Fill };
    private static SKColor ParseColor(string value)
    {
        if (value is { Length: 7 } && value[0] == '#' && uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        return SKColors.Black;
    }

    private static string NormalizeDestination(string path, out SKEncodedImageFormat format, out int quality)
    {
        var full = Path.GetFullPath(path);
        switch (Path.GetExtension(full).ToLowerInvariant())
        {
            case ".jpg":
            case ".jpeg": format = SKEncodedImageFormat.Jpeg; quality = 94; return full;
            case ".png": format = SKEncodedImageFormat.Png; quality = 100; return full;
            case "": format = SKEncodedImageFormat.Png; quality = 100; return full + ".png";
            default: throw new InvalidOperationException("Imagine image export currently supports PNG and JPEG.");
        }
    }
}
