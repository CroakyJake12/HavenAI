using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

public sealed class DocumentShapeGalleryTests : IDisposable
{
    private readonly GalleryTestPaths _paths = new();

    [Fact]
    public void Infrastructure_registers_one_shared_shape_gallery()
    {
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<DocumentShapeGalleryRepository>(provider.GetRequiredService<IDocumentShapeGallery>());
    }

    [Fact]
    public async Task Saved_shape_survives_gallery_reopen_and_inserts_as_independent_editable_geometry()
    {
        var firstRepository = new DocumentShapeGalleryRepository(_paths);
        var shape = CreateShape();
        var saved = await firstRepository.SaveAsync(new DocumentShapeGalleryItem
        {
            Category = DocumentShapeGalleryCategory.MyShapes,
            Name = "My curved badge",
            Shape = shape
        }, CancellationToken.None);

        var reopenedRepository = new DocumentShapeGalleryRepository(_paths);
        var reopened = await reopenedRepository.LoadAsync(saved.Id, CancellationToken.None);
        var personal = await reopenedRepository.ListAsync(DocumentShapeGalleryCategory.MyShapes, CancellationToken.None);

        Assert.NotNull(reopened);
        Assert.Single(personal);
        Assert.Equal(saved.Id, reopened!.Shape.GallerySourceId);
        Assert.True(DocumentVectorShapeValidator.Validate(reopened.Shape).IsValid);
        var inserted = DocumentVectorShapes.CloneForInsertion(reopened.Shape, reopened.Id);
        Assert.NotEqual(reopened.Shape.Id, inserted.Id);
        Assert.Equal(reopened.Id, inserted.GallerySourceId);
        Assert.Equal(reopened.Shape.Paths[0].Subpaths[0].Nodes.Select(node => (node.X, node.Y)), inserted.Paths[0].Subpaths[0].Nodes.Select(node => (node.X, node.Y)));
    }

    [Theory]
    [InlineData(DocumentShapeGalleryCategory.BuiltInShapes)]
    [InlineData(DocumentShapeGalleryCategory.MyShapes)]
    [InlineData(DocumentShapeGalleryCategory.WorkspaceShapes)]
    [InlineData(DocumentShapeGalleryCategory.PluginShapes)]
    public async Task Gallery_persists_each_supported_category(DocumentShapeGalleryCategory category)
    {
        var repository = new DocumentShapeGalleryRepository(_paths);
        await repository.SaveAsync(new DocumentShapeGalleryItem { Category = category, Name = category.ToString(), Shape = CreateShape() }, CancellationToken.None);

        var items = await new DocumentShapeGalleryRepository(_paths).ListAsync(category, CancellationToken.None);

        Assert.Contains(items, item => item.Category == category);
    }

    private static DocumentVectorShape CreateShape()
    {
        var shape = new DocumentVectorShape
        {
            Name = "Curved badge",
            Paths =
            [
                new DocumentVectorPath
                {
                    Subpaths =
                    [
                        new DocumentVectorSubpath
                        {
                            Closed = true,
                            Nodes =
                            [
                                new DocumentVectorNode { X = 0, Y = 0 },
                                new DocumentVectorNode { X = 100, Y = 0, IncomingSegment = DocumentVectorSegmentKind.Cubic, Control1 = new DocumentVectorPoint(25, -20), Control2 = new DocumentVectorPoint(75, 20) },
                                new DocumentVectorNode { X = 100, Y = 100 },
                                new DocumentVectorNode { X = 0, Y = 100 }
                            ]
                        }
                    ]
                }
            ]
        };
        shape.Normalize();
        return shape;
    }

    public void Dispose() => _paths.Dispose();

    private sealed class GalleryTestPaths : IAppPaths, IDisposable
    {
        public GalleryTestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-shape-gallery-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");

        public void Dispose()
        {
            try { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
