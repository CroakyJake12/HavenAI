using Haven.Desktop.Views.Pages.Imagine;
using Haven.UI;
using SkiaSharp;

namespace Haven.Desktop.Tests;

public sealed class VisionRegionCropperTests
{
    [Fact]
    public void Display_region_mapping_removes_contain_letterboxing()
    {
        var source = VisionRegionCropper.MapDisplaySelectionToSource(
            new HavenRect(0, .25, 1, .5),
            viewportWidth: 400,
            viewportHeight: 400,
            sourceWidth: 800,
            sourceHeight: 400);

        Assert.Equal(0, source.X, 6);
        Assert.Equal(0, source.Y, 6);
        Assert.Equal(1, source.Width, 6);
        Assert.Equal(1, source.Height, 6);
    }

    [Fact]
    public async Task Region_crop_writes_only_selected_source_pixels()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HavenVisionCropTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.png");
        string? cropPath = null;
        try
        {
            using (var bitmap = new SKBitmap(200, 100, SKColorType.Rgba8888, SKAlphaType.Premul))
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.White);
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = File.Create(sourcePath);
                data.SaveTo(stream);
            }

            cropPath = await VisionRegionCropper.CreateCropAsync(
                sourcePath,
                new HavenRect(.25, .25, .5, .5),
                viewportWidth: 200,
                viewportHeight: 100,
                CancellationToken.None);

            using var cropped = SKBitmap.Decode(cropPath);
            Assert.NotNull(cropped);
            Assert.Equal(100, cropped.Width);
            Assert.Equal(50, cropped.Height);
        }
        finally
        {
            VisionRegionCropper.DeleteTemporary(cropPath);
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }
}
