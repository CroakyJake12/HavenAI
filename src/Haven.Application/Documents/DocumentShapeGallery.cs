using Haven.Core;

namespace Haven.Application;

public enum DocumentShapeGalleryCategory
{
    BuiltInShapes = 0,
    MyShapes = 1,
    WorkspaceShapes = 2,
    PluginShapes = 3
}

public sealed class DocumentShapeGalleryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DocumentShapeGalleryCategory Category { get; set; } = DocumentShapeGalleryCategory.MyShapes;
    public string Name { get; set; } = "Custom shape";
    public string OwnerKey { get; set; } = string.Empty;
    public bool ReadOnly { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DocumentVectorShape Shape { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);

    public void Normalize()
    {
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        Name = string.IsNullOrWhiteSpace(Name) ? "Custom shape" : Name.Trim();
        OwnerKey ??= string.Empty;
        Shape ??= new DocumentVectorShape();
        Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
        Shape.Normalize();
        Shape.GallerySourceId = Id;
        if (CreatedAt == default) CreatedAt = DateTimeOffset.UtcNow;
        if (UpdatedAt == default) UpdatedAt = CreatedAt;
    }
}

public interface IDocumentShapeGallery
{
    Task<IReadOnlyList<DocumentShapeGalleryItem>> ListAsync(DocumentShapeGalleryCategory? category, CancellationToken cancellationToken);
    Task<DocumentShapeGalleryItem?> LoadAsync(Guid itemId, CancellationToken cancellationToken);
    Task<DocumentShapeGalleryItem> SaveAsync(DocumentShapeGalleryItem item, CancellationToken cancellationToken);
    Task DeleteAsync(Guid itemId, CancellationToken cancellationToken);
}
