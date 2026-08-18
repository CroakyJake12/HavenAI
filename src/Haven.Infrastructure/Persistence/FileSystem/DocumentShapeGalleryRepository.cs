using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class DocumentShapeGalleryRepository : IDocumentShapeGallery
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DocumentShapeGalleryRepository(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _path = Path.Combine(paths.DataDirectory, "Documents", "ShapeGallery", "gallery.json");
    }

    public async Task<IReadOnlyList<DocumentShapeGalleryItem>> ListAsync(DocumentShapeGalleryCategory? category, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var gallery = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return gallery.Items
                .Where(item => category is null || item.Category == category.Value)
                .OrderBy(item => item.Category)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(CloneItem)
                .ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<DocumentShapeGalleryItem?> LoadAsync(Guid itemId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var gallery = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var item = gallery.Items.FirstOrDefault(value => value.Id == itemId);
            return item is null ? null : CloneItem(item);
        }
        finally { _gate.Release(); }
    }

    public async Task<DocumentShapeGalleryItem> SaveAsync(DocumentShapeGalleryItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var gallery = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var copy = CloneItem(item);
            copy.Normalize();
            var validation = DocumentVectorShapeValidator.Validate(copy.Shape);
            if (!validation.IsValid) throw new InvalidDataException("The custom shape gallery item contains invalid vector geometry.");
            var existing = gallery.Items.FindIndex(value => value.Id == copy.Id);
            var now = DateTimeOffset.UtcNow;
            if (existing >= 0)
            {
                if (gallery.Items[existing].ReadOnly) throw new InvalidOperationException("This shape-gallery item is read-only.");
                copy.CreatedAt = gallery.Items[existing].CreatedAt;
                copy.UpdatedAt = now;
                gallery.Items[existing] = copy;
            }
            else
            {
                copy.CreatedAt = now; copy.UpdatedAt = now; gallery.Items.Add(copy);
            }
            await WriteUnsafeAsync(gallery, cancellationToken).ConfigureAwait(false);
            return CloneItem(copy);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid itemId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var gallery = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var existing = gallery.Items.FirstOrDefault(value => value.Id == itemId);
            if (existing is null) return;
            if (existing.ReadOnly) throw new InvalidOperationException("This shape-gallery item is read-only.");
            gallery.Items.Remove(existing);
            await WriteUnsafeAsync(gallery, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<GalleryFile> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new GalleryFile();
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        GalleryFile gallery;
        try { gallery = await JsonSerializer.DeserializeAsync<GalleryFile>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? new GalleryFile(); }
        catch (JsonException ex) { throw new InvalidDataException("The Documents custom-shape gallery is unreadable.", ex); }
        if (gallery.SchemaVersion <= 0 || gallery.SchemaVersion > GalleryFile.CurrentSchemaVersion) throw new InvalidDataException("The Documents custom-shape gallery schema is not supported.");
        gallery.Items ??= [];
        foreach (var item in gallery.Items)
        {
            item.Normalize();
            var validation = DocumentVectorShapeValidator.Validate(item.Shape);
            if (!validation.IsValid) throw new InvalidDataException($"Shape-gallery item '{item.Name}' contains invalid vector geometry.");
        }
        return gallery;
    }

    private async Task WriteUnsafeAsync(GalleryFile gallery, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)!; Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $"gallery-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, gallery, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            await using (var verify = File.OpenRead(temporary))
                _ = await JsonSerializer.DeserializeAsync<GalleryFile>(verify, JsonOptions, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("The shape gallery failed its verification read.");
            File.Move(temporary, _path, overwrite: true);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static DocumentShapeGalleryItem CloneItem(DocumentShapeGalleryItem item)
    {
        var json = JsonSerializer.Serialize(item, JsonOptions);
        return JsonSerializer.Deserialize<DocumentShapeGalleryItem>(json, JsonOptions) ?? throw new InvalidDataException("The custom shape gallery item could not be cloned.");
    }

    private sealed class GalleryFile
    {
        public const int CurrentSchemaVersion = 1;
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public List<DocumentShapeGalleryItem> Items { get; set; } = [];
    }
}
