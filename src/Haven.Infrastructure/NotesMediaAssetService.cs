using System.Diagnostics;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class NotesMediaAssetService(
    INotesAttachmentStore attachments,
    IProductionDiagnostics diagnostics) : INotesMediaAssetService
{
    public async Task<NotesMediaVerification> VerifyAsync(
        NotesMediaData media,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (media.AttachmentId == Guid.Empty)
            throw new InvalidDataException("The Notes media attachment ID is empty.");
        var path = await attachments.ResolvePathAsync(media.AttachmentId, cancellationToken).ConfigureAwait(false);
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("The managed Notes media file is missing.", fullPath);
        var hash = await ComputeSha256Async(fullPath, cancellationToken).ConfigureAwait(false);
        var verification = new NotesMediaVerification(
            fullPath,
            info.Length,
            hash,
            media.SizeBytes == info.Length,
            !string.IsNullOrWhiteSpace(media.Sha256)
            && media.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow);
        await diagnostics.WriteAsync(
            verification.SizeMatches && verification.HashMatches
                ? ReliabilitySeverity.Information
                : ReliabilitySeverity.Warning,
            "notes",
            verification.SizeMatches && verification.HashMatches
                ? "media-verified"
                : "media-integrity-mismatch",
            verification.SizeMatches && verification.HashMatches
                ? "A managed Notes media asset passed its integrity check."
                : "A managed Notes media asset did not match its recorded size or SHA-256 hash.",
            new Dictionary<string, string>
            {
                ["attachmentId"] = media.AttachmentId.ToString("D"),
                ["sizeMatches"] = verification.SizeMatches.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["hashMatches"] = verification.HashMatches.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return verification;
    }

    public async Task<NotesMediaData> ReplaceAsync(
        NotesMediaData current,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        var replacement = await attachments.ImportAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        replacement.AltText = current.AltText;
        replacement.Caption = current.Caption;
        replacement.Wrapping = current.Wrapping;
        replacement.Width = current.Width;
        replacement.Height = current.Height;
        replacement.Rotation = current.Rotation;
        replacement.CropLeft = current.CropLeft;
        replacement.CropTop = current.CropTop;
        replacement.CropRight = current.CropRight;
        replacement.CropBottom = current.CropBottom;
        await diagnostics.WriteAsync(
            ReliabilitySeverity.Information,
            "notes",
            "media-replaced",
            "A Notes media block was replaced through the managed attachment store.",
            new Dictionary<string, string>
            {
                ["oldAttachmentId"] = current.AttachmentId.ToString("D"),
                ["newAttachmentId"] = replacement.AttachmentId.ToString("D")
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return replacement;
    }

    public async Task<string> SaveCopyAsync(
        NotesMediaData media,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("A destination path is required.", nameof(destinationPath));
        var verification = await VerifyAsync(media, cancellationToken).ConfigureAwait(false);
        if (!verification.SizeMatches || !verification.HashMatches)
            throw new InvalidDataException("The media asset failed integrity verification and cannot be copied.");
        var destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var input = new FileStream(
                verification.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            var copiedHash = await ComputeSha256Async(temporary, cancellationToken).ConfigureAwait(false);
            if (!copiedHash.Equals(verification.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The copied media did not match the verified source hash.");
            if (File.Exists(destination)) File.Replace(temporary, destination, destination + ".bak", true);
            else File.Move(temporary, destination);
            return destination;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public async Task OpenAsync(NotesMediaData media, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        var verification = await VerifyAsync(media, cancellationToken).ConfigureAwait(false);
        if (!verification.SizeMatches || !verification.HashMatches)
            throw new InvalidDataException("The media asset failed integrity verification and was not opened.");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = verification.Path,
                UseShellExecute = true
            }) ?? throw new InvalidOperationException("The operating system did not accept the media open request.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("The verified media file could not be opened by the operating system.", ex);
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
            .ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
