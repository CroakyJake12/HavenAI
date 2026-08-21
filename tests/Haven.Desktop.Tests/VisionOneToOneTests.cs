using Haven.Desktop.Views.Pages.Imagine;
using Haven.UI;
using SkiaSharp;

namespace Haven.Desktop.Tests;

public sealed class VisionOneToOneTests
{
    [Fact]
    public void One_to_one_zoom_accounts_for_os_render_scaling()
    {
        var zoom = VisionPreviewElement.CalculateOneToOneZoom(400, 225, 1600, 900, 2);
        Assert.Equal(2, zoom, 6);
    }

    [Fact]
    public void Vision_scene_can_attach_a_real_one_to_one_action()
    {
        var scene = new VisionScene();
        var button = VisionPage.AttachOneToOneButton(scene, () => 1);
        Assert.Equal("Vision.OneToOne", button.Name);
        Assert.Equal("1:1", button.Content);
        Assert.Contains(button, scene.Root.DescendantsAndSelf());
    }

    [Fact]
    public void Intrinsic_dimensions_are_read_without_full_raster_rendering()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HavenVisionMetadataTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sample.png");
        try
        {
            using (var bitmap = new SKBitmap(320, 180, SKColorType.Rgba8888, SKAlphaType.Premul))
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.White);
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = File.Create(path);
                data.SaveTo(stream);
            }

            Assert.True(VisionImageMetadata.TryRead(path, out var info));
            Assert.Equal(320, info.Width);
            Assert.Equal(180, info.Height);
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }
}
