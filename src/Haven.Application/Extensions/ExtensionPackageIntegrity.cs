using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Computes and verifies the immutable content hash used to trust installed extension packages.
/// </summary>
public static class ExtensionPackageIntegrity
{
    public static async Task VerifyInstalledAsync(
        InstalledExtensionPackage package,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!Directory.Exists(package.InstallPath))
            throw new InvalidDataException("Installed extension package content is missing.");

        var currentHash = await ComputeHashAsync(package.InstallPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(currentHash, package.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installed extension package failed its content-integrity check and will not be executed.");
    }

    public static async Task<string> ComputeHashAsync(string root, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Extension package roots cannot be linked directories.");

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Extension packages cannot contain linked directories.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var frame = new byte[12];
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(file => !Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar)
                         .Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase)))
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
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                hash.AppendData(buffer.AsSpan(0, read));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
