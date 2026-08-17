using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Source-preserving Imagine persistence. Imported media is copied into Haven's
/// managed data directory; project manifests are written atomically.
/// </summary>
public sealed class ImagineProjectRepository(IAppPaths paths) : IImagineProjectRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private string Root => Path.Combine(paths.DataDirectory, "Imagine");
    private string Projects => Path.Combine(Root, "Projects");
    private string Assets => Path.Combine(Root, "Assets");

    public async Task<ImagineProject> CreateAsync(
        string name,
        double canvasWidth,
        double canvasHeight,
        CancellationToken cancellationToken)
    {
        var project = ImagineProjectSession.CreateProject(name, canvasWidth, canvasHeight);
        await SaveAsync(project, cancellationToken).ConfigureAwait(false);
        return project;
    }

    public async Task<ImagineProject?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var path = ProjectPath(id);
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        return await JsonSerializer.DeserializeAsync<ImagineProject>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Imagine project '{id}' is empty or invalid.");
    }

    public async Task<IReadOnlyList<ImagineProject>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Projects);
        var rows = new List<ImagineProject>();
        foreach (var path in Directory.EnumerateFiles(Projects, "*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(Math.Clamp(limit, 1, 200)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
                if (await JsonSerializer.DeserializeAsync<ImagineProject>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) is { } item)
                    rows.Add(item);
            }
            catch (JsonException)
            {
                // One damaged project must not hide healthy recent projects.
            }
        }
        return rows.OrderByDescending(item => item.UpdatedAt).Take(Math.Clamp(limit, 1, 200)).ToArray();
    }

    public async Task SaveAsync(ImagineProject project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        Directory.CreateDirectory(Projects);
        var destination = ProjectPath(project.Id);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, project, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(destination))
                File.Replace(temporary, destination, null, true);
            else
                File.Move(temporary, destination);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public async Task<ImagineMediaAsset> ImportAssetAsync(
        Guid projectId,
        string sourcePath,
        ImagineMediaKind kind,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("An import path is required.", nameof(sourcePath));
        var source = Path.GetFullPath(sourcePath);
        var info = new FileInfo(source);
        if (!info.Exists) throw new FileNotFoundException("The selected media file does not exist.", source);
        if (info.Length <= 0) throw new InvalidDataException("The selected media file is empty.");

        var assetId = Guid.NewGuid();
        var folder = Path.Combine(Assets, projectId.ToString("N"));
        Directory.CreateDirectory(folder);
        var extension = Path.GetExtension(info.Name);
        var destination = Path.Combine(folder, assetId.ToString("N") + extension.ToLowerInvariant());
        var temporary = destination + ".tmp";
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            var sourceHash = await HashAsync(source, cancellationToken).ConfigureAwait(false);
            var copyHash = await HashAsync(temporary, cancellationToken).ConfigureAwait(false);
            if (!sourceHash.Equals(copyHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The managed Imagine copy did not match the selected source media.");
            File.Move(temporary, destination);
            return new ImagineMediaAsset(
                assetId,
                kind,
                info.Name,
                source,
                destination,
                info.Length,
                sourceHash,
                DateTimeOffset.UtcNow,
                JsonSerializer.Serialize(new { extension = extension.ToLowerInvariant() }));
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public async Task<string> ExportBundleAsync(
        ImagineProject project,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("An export path is required.", nameof(destinationPath));
        var destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, true))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifest = archive.CreateEntry("project.json", CompressionLevel.Optimal);
                await using (var manifestStream = manifest.Open())
                    await JsonSerializer.SerializeAsync(manifestStream, project, JsonOptions, cancellationToken).ConfigureAwait(false);

                foreach (var asset in project.Assets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!File.Exists(asset.ManagedPath)) continue;
                    var entry = archive.CreateEntry(
                        "assets/" + asset.Id.ToString("N") + Path.GetExtension(asset.ManagedPath).ToLowerInvariant(),
                        CompressionLevel.Optimal);
                    await using var source = new FileStream(asset.ManagedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
                    await using var target = entry.Open();
                    await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                }
            }

            if (File.Exists(destination))
                File.Replace(temporary, destination, null, true);
            else
                File.Move(temporary, destination);
            return destination;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private string ProjectPath(Guid id)
    {
        Directory.CreateDirectory(Projects);
        return Path.Combine(Projects, id.ToString("N") + ".json");
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
