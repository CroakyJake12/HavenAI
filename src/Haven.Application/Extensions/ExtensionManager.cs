using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Repository-backed package discovery and atomic installation coordinator.</summary>
public sealed class ExtensionManager(
    IExtensionRepository repository,
    IExtensionSourceTransport transport,
    ExtensionManifestValidator validator,
    NativePluginRuntime runtime,
    IAppPaths paths)
{
    private static readonly string[] ManifestLocations = ["haven.repository.json", ".haven/repository.json"];

    public Task<IReadOnlyList<ExtensionSource>> GetSourcesAsync(CancellationToken cancellationToken) => repository.GetSourcesAsync(cancellationToken);
    public Task<IReadOnlyList<InstalledExtensionPackage>> GetInstalledAsync(CancellationToken cancellationToken) => repository.GetInstalledAsync(cancellationToken);

    public Task RemoveSourceAsync(Guid sourceId, CancellationToken cancellationToken) => repository.DeleteSourceAsync(sourceId, cancellationToken);

    public async Task AddSourceAsync(ExtensionSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Type == ExtensionSourceType.GitHubRepository && !IsGitHubUri(source.RepositoryUri))
            throw new ArgumentException("GitHub sources must use an HTTPS or SSH github.com repository URL.", nameof(source));
        if (source.IsPrivate && string.IsNullOrWhiteSpace(source.ConnectedAccountId))
            throw new InvalidOperationException("Private repositories require an authorised connected GitHub account reference.");
        await repository.UpsertSourceAsync(source, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DiscoveredExtensionPackage>> RefreshAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        var source = (await repository.GetSourcesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == sourceId)
            ?? throw new KeyNotFoundException("Extension source was not found.");
        var refreshRoot = Path.Combine(paths.DataDirectory, "extension-sources", source.Id.ToString("N"), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(refreshRoot);
        var materialized = await transport.MaterializeAsync(source, refreshRoot, cancellationToken).ConfigureAwait(false);
        var manifestPath = ManifestLocations.Select(location => Path.Combine(materialized, location.Replace('/', Path.DirectorySeparatorChar)))
            .FirstOrDefault(File.Exists) ?? throw new InvalidDataException("Repository does not contain haven.repository.json or .haven/repository.json.");
        var document = JsonSerializer.Deserialize<ExtensionManifestDocument>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Repository manifest was empty.");
        var validation = validator.Validate(document);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(Environment.NewLine, validation.Errors));
        var installed = await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false);
        var discovered = new List<DiscoveredExtensionPackage>(document.Packages.Count);
        foreach (var package in document.Packages)
        {
            var packageRoot = ResolveInside(materialized, package.PackagePath);
            if (!Directory.Exists(packageRoot)) throw new InvalidDataException($"Package directory '{package.PackagePath}' does not exist.");
            var hash = await HashDirectoryAsync(packageRoot, cancellationToken).ConfigureAwait(false);
            var current = installed.FirstOrDefault(item => item.Manifest.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase));
            var state = current is null ? ExtensionInstallState.Available
                : Version.TryParse(package.Version, out var available) && Version.TryParse(current.Manifest.Version, out var present) && available > present
                    ? ExtensionInstallState.UpdateAvailable : current.State;
            discovered.Add(new DiscoveredExtensionPackage(source.Id, package, materialized, hash, state));
        }
        await repository.UpsertSourceAsync(source with { LastRefreshedAt = DateTimeOffset.UtcNow, SafeLastError = null }, cancellationToken).ConfigureAwait(false);
        return discovered;
    }

    public async Task<InstalledExtensionPackage> InstallAsync(
        DiscoveredExtensionPackage discovered,
        ExtensionPermission explicitlyGrantedPermissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        if (explicitlyGrantedPermissions != discovered.Manifest.RequestedPermissions)
            throw new UnauthorizedAccessException("All requested package permissions must be reviewed and granted explicitly.");
        var installed = await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false);
        var existing = installed.FirstOrDefault(item => item.Manifest.PackageId.Equals(discovered.Manifest.PackageId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var currentHash = Directory.Exists(existing.InstallPath)
                ? await HashDirectoryAsync(existing.InstallPath, cancellationToken).ConfigureAwait(false) : string.Empty;
            if (existing.HasLocalModifications || !string.Equals(currentHash, existing.ContentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Installed package has local modifications and will not be overwritten.");
            var broader = discovered.Manifest.RequestedPermissions & ~existing.GrantedPermissions;
            if (broader != ExtensionPermission.None)
                throw new UnauthorizedAccessException("The update requests broader permissions and requires a separate permission review.");
            await runtime.UnloadAsync(existing.Manifest.PackageId, cancellationToken).ConfigureAwait(false);
        }
        var packageSource = ResolveInside(discovered.MaterializedRepositoryPath, discovered.Manifest.PackagePath);
        var destination = Path.Combine(paths.DataDirectory, "extensions", discovered.Manifest.PackageId, discovered.Manifest.Version);
        var staging = destination + ".installing-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        var movedIntoStore = false;
        var loadedNewPackage = false;
        try
        {
            CopyDirectory(packageSource, staging);
            var stagedHash = await HashDirectoryAsync(staging, cancellationToken).ConfigureAwait(false);
            if (!stagedHash.Equals(discovered.ContentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package contents changed during installation.");
            if (Directory.Exists(destination)) throw new IOException("This package version is already installed.");
            Directory.Move(staging, destination);
            movedIntoStore = true;
            var now = DateTimeOffset.UtcNow;
            var package = new InstalledExtensionPackage(
                existing?.Id ?? Guid.NewGuid(), discovered.SourceId, discovered.Manifest, destination,
                explicitlyGrantedPermissions, ExtensionInstallState.Installed, true, false,
                discovered.ContentHash, existing?.InstalledAt ?? now, now);
            // Skill-only packages use the same registration path but start no executable capability.
            await runtime.LoadAsync(package, cancellationToken).ConfigureAwait(false);
            loadedNewPackage = true;
            await repository.UpsertInstalledAsync(package, cancellationToken).ConfigureAwait(false);
            return package;
        }
        catch
        {
            if (loadedNewPackage)
                try { await runtime.UnloadAsync(discovered.Manifest.PackageId, CancellationToken.None).ConfigureAwait(false); } catch { }
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (movedIntoStore && Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            if (existing is { IsEnabled: true })
                try { await runtime.LoadAsync(existing, CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    public async Task SetEnabledAsync(Guid packageId, bool enabled, CancellationToken cancellationToken)
    {
        var package = (await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == packageId)
            ?? throw new KeyNotFoundException("Installed package was not found.");
        var updated = package with
        {
            IsEnabled = enabled,
            State = enabled ? ExtensionInstallState.Installed : ExtensionInstallState.Disabled,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        if (enabled)
        {
            await runtime.LoadAsync(updated, cancellationToken).ConfigureAwait(false);
            try { await repository.UpsertInstalledAsync(updated, cancellationToken).ConfigureAwait(false); }
            catch
            {
                try { await runtime.UnloadAsync(package.Manifest.PackageId, CancellationToken.None).ConfigureAwait(false); } catch { }
                throw;
            }
            return;
        }

        await runtime.UnloadAsync(package.Manifest.PackageId, cancellationToken).ConfigureAwait(false);
        try { await repository.UpsertInstalledAsync(updated, cancellationToken).ConfigureAwait(false); }
        catch
        {
            if (package.IsEnabled)
                try { await runtime.LoadAsync(package, CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    public async Task UninstallAsync(Guid packageId, CancellationToken cancellationToken)
    {
        var package = (await repository.GetInstalledAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == packageId)
            ?? throw new KeyNotFoundException("Installed package was not found.");
        var extensionRoot = Path.GetFullPath(Path.Combine(paths.DataDirectory, "extensions")) + Path.DirectorySeparatorChar;
        var installPath = Path.GetFullPath(package.InstallPath);
        if (!installPath.StartsWith(extensionRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Installed package path is outside Haven's extension store.");
        var quarantine = installPath + ".uninstalling-" + Guid.NewGuid().ToString("N");
        var moved = false;
        await runtime.UnloadAsync(package.Manifest.PackageId, cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(installPath))
            {
                Directory.Move(installPath, quarantine);
                moved = true;
            }
            await repository.DeleteInstalledAsync(package.Id, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (moved && Directory.Exists(quarantine) && !Directory.Exists(installPath)) Directory.Move(quarantine, installPath);
            if (package.IsEnabled)
                try { await runtime.LoadAsync(package, CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
        if (moved && Directory.Exists(quarantine))
            try { Directory.Delete(quarantine, recursive: true); } catch { /* Renamed package remains inert and can be cleaned on maintenance. */ }
    }

    private static bool IsGitHubUri(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        ? uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.Scheme == Uri.UriSchemeHttps
        : value.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase);

    private static string ResolveInside(string root, string relative)
    {
        var resolvedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package path escaped the repository.");
        return resolved;
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Extension packages cannot contain linked directories.");
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Extension packages cannot contain linked files.");
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase))) continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static async Task<string> HashDirectoryAsync(string root, CancellationToken cancellationToken)
    {
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Extension package roots cannot be linked directories.");
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Extension packages cannot contain linked directories.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var frame = new byte[12];
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(file => !Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar).Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(file => Path.GetRelativePath(root, file), StringComparer.Ordinal))
        {
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Extension packages cannot contain linked files.");
            var relative = Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace('\\', '/'));
            BinaryPrimitives.WriteInt32LittleEndian(frame, relative.Length);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(4), new FileInfo(file).Length);
            hash.AppendData(frame);
            hash.AppendData(relative);
            await using var stream = File.OpenRead(file);
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0) hash.AppendData(buffer.AsSpan(0, read));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
