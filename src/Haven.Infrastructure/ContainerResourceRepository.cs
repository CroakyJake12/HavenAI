using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using System.Xml.Linq;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed class ContainerResourceRepository(IAppPaths paths, ISqliteConnectionFactory factory) : IContainerResourceRepository
{
    public const long MaximumResourceBytes = 25L * 1024 * 1024;
    private const int MaximumPromptCharacters = 180_000;

    private static readonly IReadOnlyDictionary<string, ResourceType> SupportedTypes =
        new Dictionary<string, ResourceType>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = Text("text/plain"), [".md"] = Text("text/markdown"), [".markdown"] = Text("text/markdown"),
            [".json"] = Text("application/json"), [".csv"] = Text("text/csv"), [".tsv"] = Text("text/tab-separated-values"),
            [".xml"] = Text("application/xml"), [".yaml"] = Text("application/yaml"), [".yml"] = Text("application/yaml"),
            [".cs"] = Text("text/x-csharp"), [".fs"] = Text("text/x-fsharp"), [".vb"] = Text("text/x-vb"),
            [".xaml"] = Text("application/xml"), [".axaml"] = Text("application/xml"),
            [".js"] = Text("text/javascript"), [".jsx"] = Text("text/javascript"), [".ts"] = Text("text/typescript"),
            [".tsx"] = Text("text/typescript"), [".py"] = Text("text/x-python"), [".java"] = Text("text/x-java"),
            [".c"] = Text("text/x-c"), [".h"] = Text("text/x-c"), [".cpp"] = Text("text/x-c++"), [".hpp"] = Text("text/x-c++"),
            [".css"] = Text("text/css"), [".html"] = Text("text/html"), [".htm"] = Text("text/html"),
            [".sql"] = Text("application/sql"), [".sh"] = Text("application/x-sh"), [".ps1"] = Text("text/x-powershell"),
            [".pdf"] = Document("application/pdf"),
            [".docx"] = Document("application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            [".png"] = Image("image/png"), [".jpg"] = Image("image/jpeg"), [".jpeg"] = Image("image/jpeg"),
            [".webp"] = Image("image/webp"), [".gif"] = Image("image/gif"), [".bmp"] = Image("image/bmp")
        };

    public async Task<IReadOnlyList<ContainerResource>> GetByContainerAsync(Guid containerId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM container_resources WHERE container_id=$containerId ORDER BY created_at,name;";
        command.Parameters.AddWithValue("$containerId", containerId.ToString());
        return await ReadAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ContainerResource> AddAsync(Guid containerId, string sourcePath, CancellationToken cancellationToken)
    {
        if (containerId == Guid.Empty) throw new ArgumentException("The container identifier cannot be empty.", nameof(containerId));
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("Choose a reference file.", nameof(sourcePath));

        var fullPath = Path.GetFullPath(sourcePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists) throw new FileNotFoundException("The reference file no longer exists.", fullPath);
        if (file.Length > MaximumResourceBytes)
            throw new InvalidOperationException($"{file.Name} is larger than the 25 MB Chat Group reference limit.");
        var extension = file.Extension.ToLowerInvariant();
        if (!SupportedTypes.TryGetValue(extension, out var type))
            throw new InvalidOperationException($"{extension} files are not supported as Chat Group references.");

        string hash;
        await using (var input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();

        if (await FindByHashAsync(containerId, hash, cancellationToken).ConfigureAwait(false) is { } existing)
            return existing;

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var storedName = id.ToString("N") + extension;
        var item = new ContainerResource(id, containerId, file.Name, storedName, type.MediaType, type.Kind, file.Length, hash, now);
        var destination = GetStoredPath(item);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = destination + ".tmp";

        try
        {
            await using (var input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination);

            await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO container_resources(id,container_id,name,stored_name,media_type,kind,size_bytes,sha256,created_at)
                VALUES($id,$containerId,$name,$storedName,$mediaType,$kind,$sizeBytes,$sha256,$createdAt);
                """;
            AddParameters(command, item);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return item;
        }
        catch
        {
            TryDelete(temporary);
            TryDelete(destination);
            throw;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        ContainerResource? item;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT * FROM container_resources WHERE id=$id;";
            read.Parameters.AddWithValue("$id", id.ToString());
            item = (await ReadAsync(read, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        }
        if (item is null) return;
        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM container_resources WHERE id=$id;";
            delete.Parameters.AddWithValue("$id", id.ToString());
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        TryDelete(GetStoredPath(item));
    }

    public string GetStoredPath(ContainerResource resource)
    {
        if (!string.Equals(resource.StoredName, Path.GetFileName(resource.StoredName), StringComparison.Ordinal))
            throw new InvalidOperationException("The stored resource name is invalid.");
        return Path.Combine(paths.DataDirectory, "container-resources", resource.ContainerId.ToString("N"), resource.StoredName);
    }

    public async Task<string> BuildPromptContextAsync(Guid containerId, CancellationToken cancellationToken)
    {
        var resources = await GetByContainerAsync(containerId, cancellationToken).ConfigureAwait(false);
        if (resources.Count == 0) return string.Empty;
        var result = new StringBuilder("Chat Group reference files:");
        foreach (var resource in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Append("\n- ").Append(resource.Name).Append(" (").Append(resource.MediaType).Append(")");
            if (resource.Kind != ContainerResourceKind.Text && !resource.StoredName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) continue;
            var remaining = MaximumPromptCharacters - result.Length;
            if (remaining <= 0) break;
            var path = GetStoredPath(resource);
            if (!File.Exists(path)) continue;
            var text = resource.Kind == ContainerResourceKind.Text
                ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
                : await ReadDocxTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (text.Length > remaining) text = text[..remaining] + "\n[reference truncated by Haven]";
            result.Append("\n\nReference: ").Append(resource.Name).Append("\n```\n").Append(text).Append("\n```");
        }
        return result.ToString();
    }

    private static async Task<string> ReadDocxTextAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
            var entry = archive.GetEntry("word/document.xml");
            if (entry is null) return string.Empty;
            await using var xmlStream = entry.Open();
            var document = await XDocument.LoadAsync(xmlStream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
            XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            return string.Join("\n", document.Descendants(word + "p")
                .Select(paragraph => string.Concat(paragraph.Descendants(word + "t").Select(node => node.Value)))
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Xml.XmlException)
        {
            return string.Empty;
        }
    }

    private async Task<ContainerResource?> FindByHashAsync(Guid containerId, string hash, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM container_resources WHERE container_id=$containerId AND sha256=$sha256 LIMIT 1;";
        command.Parameters.AddWithValue("$containerId", containerId.ToString());
        command.Parameters.AddWithValue("$sha256", hash);
        return (await ReadAsync(command, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    private static async Task<IReadOnlyList<ContainerResource>> ReadAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var result = new List<ContainerResource>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ContainerResource(reader.Guid("id"), reader.Guid("container_id"), reader.String("name"),
                reader.String("stored_name"), reader.String("media_type"), (ContainerResourceKind)reader.Int32("kind"),
                reader.GetInt64(reader.GetOrdinal("size_bytes")), reader.String("sha256"), reader.DateTimeOffset("created_at")));
        return result;
    }

    private static void AddParameters(SqliteCommand command, ContainerResource item)
    {
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$containerId", item.ContainerId.ToString());
        command.Parameters.AddWithValue("$name", item.Name);
        command.Parameters.AddWithValue("$storedName", item.StoredName);
        command.Parameters.AddWithValue("$mediaType", item.MediaType);
        command.Parameters.AddWithValue("$kind", (int)item.Kind);
        command.Parameters.AddWithValue("$sizeBytes", item.SizeBytes);
        command.Parameters.AddWithValue("$sha256", item.Sha256);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt.ToString("O"));
    }

    private static ResourceType Text(string mediaType) => new(mediaType, ContainerResourceKind.Text);
    private static ResourceType Document(string mediaType) => new(mediaType, ContainerResourceKind.Document);
    private static ResourceType Image(string mediaType) => new(mediaType, ContainerResourceKind.Image);
    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private sealed record ResourceType(string MediaType, ContainerResourceKind Kind);
}
