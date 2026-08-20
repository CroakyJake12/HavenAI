using Haven.UI;
using SkiaSharp;

namespace Haven.Desktop.Views.Pages.Imagine;

/// <summary>Maps Vision display selections back to source pixels and creates temporary lossless crops.</summary>
internal static class VisionRegionCropper
{
    private const string RegionMarker = "\n\nThe user selected an approximate display region of the attached image:";
    private const long MaxDecodedPixels = 100_000_000;

    internal static bool IsRegionPrompt(string prompt) =>
        !string.IsNullOrWhiteSpace(prompt) && prompt.Contains(RegionMarker, StringComparison.Ordinal);

    internal static string GetRegionQuestion(string prompt)
    {
        var marker = prompt.IndexOf(RegionMarker, StringComparison.Ordinal);
        var question = marker >= 0 ? prompt[..marker] : prompt;
        question = question.Trim();
        return (string.IsNullOrWhiteSpace(question) ? "Describe what is visible in this selected region." : question) +
               "\n\nThe attached image contains only the exact region selected by the user.";
    }

    internal static HavenRect MapDisplaySelectionToSource(HavenRect selection, double viewportWidth, double viewportHeight, int sourceWidth, int sourceHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
            throw new InvalidOperationException("Vision cannot map this region because the image dimensions are unavailable.");

        var scale = Math.Min(viewportWidth / sourceWidth, viewportHeight / sourceHeight);
        var displayedWidth = sourceWidth * scale;
        var displayedHeight = sourceHeight * scale;
        var content = new HavenRect(
            (viewportWidth - displayedWidth) / (2 * viewportWidth),
            (viewportHeight - displayedHeight) / (2 * viewportHeight),
            displayedWidth / viewportWidth,
            displayedHeight / viewportHeight);

        var left = Math.Max(selection.X, content.X);
        var top = Math.Max(selection.Y, content.Y);
        var right = Math.Min(selection.X + selection.Width, content.X + content.Width);
        var bottom = Math.Min(selection.Y + selection.Height, content.Y + content.Height);
        if (right <= left || bottom <= top)
            throw new InvalidOperationException("The selected region contains only preview padding. Select an area over the image itself.");

        return new HavenRect(
            Math.Clamp((left - content.X) / content.Width, 0, 1),
            Math.Clamp((top - content.Y) / content.Height, 0, 1),
            Math.Clamp((right - left) / content.Width, 0, 1),
            Math.Clamp((bottom - top) / content.Height, 0, 1));
    }

    internal static async Task<string> CreateCropAsync(string sourcePath, HavenRect displaySelection, double viewportWidth, double viewportHeight, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The Vision source image is unavailable.", sourcePath);
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = SKBitmap.Decode(sourcePath) ?? throw new InvalidDataException("Vision could not decode the selected image.");
        if (bitmap.Width <= 0 || bitmap.Height <= 0 || (long)bitmap.Width * bitmap.Height > MaxDecodedPixels)
            throw new InvalidOperationException($"This {bitmap.Width}×{bitmap.Height} image is too large for region cropping.");

        var region = MapDisplaySelectionToSource(displaySelection, viewportWidth, viewportHeight, bitmap.Width, bitmap.Height);
        var left = Math.Clamp((int)Math.Floor(region.X * bitmap.Width), 0, bitmap.Width - 1);
        var top = Math.Clamp((int)Math.Floor(region.Y * bitmap.Height), 0, bitmap.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling((region.X + region.Width) * bitmap.Width), left + 1, bitmap.Width);
        var bottom = Math.Clamp((int)Math.Ceiling((region.Y + region.Height) * bitmap.Height), top + 1, bitmap.Height);
        var cropWidth = right - left;
        var cropHeight = bottom - top;

        using var cropped = new SKBitmap(cropWidth, cropHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(cropped))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(bitmap, new SKRect(left, top, right, bottom), new SKRect(0, 0, cropWidth, cropHeight));
            canvas.Flush();
        }
        using var image = SKImage.FromBitmap(cropped);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("Vision could not encode the selected region.");
        var directory = Path.Combine(Path.GetTempPath(), "Haven", "Vision", "regions");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "region-" + Guid.NewGuid().ToString("N") + ".png");
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
        data.SaveTo(stream);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        return path;
    }

    internal static void DeleteTemporary(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
