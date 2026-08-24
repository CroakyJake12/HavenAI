using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Imagine;

namespace Haven.Desktop.Tests;

public sealed class ImagineRasterExporterTests
{
    [Fact]
    public async Task Image_export_writes_real_png_from_committed_canvas_objects()
    {
        var root = Path.Combine(Path.GetTempPath(), "haven-imagine-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var session = new ImagineProjectSession(ImagineProjectSession.CreateProject("Raster", 320, 240));
            session.AddRectangle(20, 30, 140, 90);
            session.AddText("Haven", 80, 120);
            Assert.True(session.RotateSelected(12));
            var path = await ImagineRasterExporter.ExportAsync(session.Project, Path.Combine(root, "result.png"), TestContext.Current.CancellationToken);
            var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            Assert.True(bytes.Length > 100);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes.Take(8).ToArray());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
