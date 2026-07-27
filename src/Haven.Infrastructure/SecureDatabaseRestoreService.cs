/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/SecureDatabaseRestoreService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns SecureDatabaseRestoreService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Represents secure database restore service and keeps its related state and behavior together.
/// </summary>
public sealed class SecureDatabaseRestoreService(
    DatabaseRestoreService inner,
    IAppPaths paths,
    IProductionDiagnostics diagnostics) : IDatabaseRestoreService
{
    /// <summary>
    /// Stores backup root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _backupRoot = Path.GetFullPath(Path.Combine(paths.DataDirectory, "Backups"))
        .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

    /// <summary>
    /// Retrieves backups async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ManagedDatabaseBackup>> GetBackupsAsync(CancellationToken cancellationToken)
    {
        var backups = await inner.GetBackupsAsync(cancellationToken).ConfigureAwait(false);
        return backups.Select(ApplyTrustBoundary).ToArray();
    }

    /// <summary>
    /// Retrieves pending async for the current operation.
    /// </summary>
    public Task<PendingDatabaseRestore?> GetPendingAsync(CancellationToken cancellationToken) =>
        inner.GetPendingAsync(cancellationToken);

    /// <summary>
    /// Performs request restore asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<PendingDatabaseRestore> RequestRestoreAsync(string backupFileName, CancellationToken cancellationToken)
    {
        var backup = (await GetBackupsAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.FileName.Equals(backupFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("The selected managed Haven backup does not exist.", backupFileName);
        if (!backup.IsVerified)
        {
            await LogBlockedAsync(backup, "request", cancellationToken).ConfigureAwait(false);
            throw new InvalidDataException("The selected backup is outside Haven's trusted managed-backup boundary: " + backup.VerificationMessage);
        }
        return await inner.RequestRestoreAsync(backup.FileName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports whether cancel pending async is true for the current state.
    /// </summary>
    public Task CancelPendingAsync(CancellationToken cancellationToken) =>
        inner.CancelPendingAsync(cancellationToken);

    /// <summary>
    /// Performs apply pending restore asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<DatabaseRestoreResult?> ApplyPendingRestoreAsync(CancellationToken cancellationToken)
    {
        var pending = await inner.GetPendingAsync(cancellationToken).ConfigureAwait(false);
        if (pending is null) return null;
        var backup = (await GetBackupsAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.FileName.Equals(pending.BackupFileName, StringComparison.OrdinalIgnoreCase));
        if (backup is null || !backup.IsVerified)
        {
            if (backup is not null) await LogBlockedAsync(backup, "apply", cancellationToken).ConfigureAwait(false);
            await inner.CancelPendingAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidDataException("The pending restore no longer points to a trusted managed Haven backup. The request was cancelled without changing the database.");
        }
        return await inner.ApplyPendingRestoreAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the apply trust boundary step owned by this component.
    /// </summary>
    private ManagedDatabaseBackup ApplyTrustBoundary(ManagedDatabaseBackup backup)
    {
        if (!backup.IsVerified) return backup;
        try
        {
            var databasePath = Path.GetFullPath(backup.DatabasePath);
            var manifestPath = Path.GetFullPath(backup.ManifestPath);
            if (!databasePath.StartsWith(_backupRoot, StringComparison.OrdinalIgnoreCase)
                || !manifestPath.StartsWith(_backupRoot, StringComparison.OrdinalIgnoreCase))
                return Reject(backup, "Backup or manifest is outside Haven's managed backup directory.");

            var databaseRelative = databasePath[_backupRoot.Length..];
            var manifestRelative = manifestPath[_backupRoot.Length..];
            if (databaseRelative.Contains(Path.DirectorySeparatorChar)
                || manifestRelative.Contains(Path.DirectorySeparatorChar)
                || databaseRelative.Contains(Path.AltDirectorySeparatorChar)
                || manifestRelative.Contains(Path.AltDirectorySeparatorChar))
                return Reject(backup, "Backup and manifest must be direct children of Haven's managed backup directory.");

            var expectedDatabaseName = Path.GetFileNameWithoutExtension(manifestPath) + ".db";
            if (!expectedDatabaseName.Equals(backup.FileName, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(databasePath).Equals(backup.FileName, StringComparison.OrdinalIgnoreCase))
                return Reject(backup, "Manifest and database filenames do not match.");

            if (IsReparsePoint(databasePath) || IsReparsePoint(manifestPath))
                return Reject(backup, "Symbolic links and reparse points are not accepted as managed backups.");
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Reject(backup, "Backup trust validation failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    /// Performs log blocked asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task LogBlockedAsync(ManagedDatabaseBackup backup, string phase, CancellationToken cancellationToken)
    {
        await diagnostics.WriteAsync(
            ReliabilitySeverity.Warning,
            "database",
            "restore-trust-boundary-blocked",
            "A database restore was blocked by the managed-backup trust boundary.",
            new Dictionary<string, string>
            {
                ["fileName"] = backup.FileName,
                ["phase"] = phase,
                ["reason"] = backup.VerificationMessage
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports whether reparse point applies to the current state.
    /// </summary>
    private static bool IsReparsePoint(string path) =>
        File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    /// <summary>
    /// Performs the reject step owned by this component.
    /// </summary>
    private static ManagedDatabaseBackup Reject(ManagedDatabaseBackup backup, string reason) =>
        backup with { IsVerified = false, VerificationMessage = reason };
}
