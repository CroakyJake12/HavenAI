/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Browser/BrowserPrivateProfileManager.cs, in the Browser layer, which isolates browser state, safety policy, transport, and automation.
 * What: This file owns BrowserPrivateProfileManager. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Browser capabilities are isolated behind explicit policy boundaries because navigation and automation process untrusted external content.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Browser;

/// <summary>
/// Represents browser private profile manager and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserPrivateProfileManager
{
    /// <summary>
    /// Stores private directory name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const string PrivateDirectoryName = "private-profiles";
    /// <summary>
    /// Stores deleting prefix locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const string DeletingPrefix = ".deleting-";
    /// <summary>
    /// Stores cleanup retry delay locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly TimeSpan CleanupRetryDelay = TimeSpan.FromMilliseconds(125);
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root;
    /// <summary>
    /// Stores root with separator locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _rootWithSeparator;
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BrowserPrivateProfileManager(string standardProfileDirectory)
    {
        if (string.IsNullOrWhiteSpace(standardProfileDirectory))
            throw new ArgumentException("A standard Browser profile directory is required.", nameof(standardProfileDirectory));

        var standard = Path.GetFullPath(standardProfileDirectory);
        RejectReparsePointsInExistingPath(
            standard,
            "The standard Browser profile path cannot contain a symbolic link or junction.");
        _root = Path.GetFullPath(Path.Combine(standard, PrivateDirectoryName));
        _rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Gets or updates root directory, the bindable or domain state represented by this property.
    /// </summary>
    public string RootDirectory => _root;

    /// <summary>
    /// Retrieves profile directory for the current operation.
    /// </summary>
    public string GetProfileDirectory(Guid tabId)
    {
        if (tabId == Guid.Empty) throw new ArgumentException("A private tab ID is required.", nameof(tabId));
        return EnsureContained(Path.Combine(_root, tabId.ToString("N")));
    }

    /// <summary>
    /// Creates async with the invariants required by its callers.
    /// </summary>
    public async Task<string> CreateAsync(Guid tabId, CancellationToken cancellationToken)
    {
        var path = GetProfileDirectory(tabId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePointsInExistingPath(
                _root,
                "The private Browser profile root path cannot contain a symbolic link or junction.");
            Directory.CreateDirectory(_root);
            RejectReparsePointsInExistingPath(
                _root,
                "The private Browser profile root path cannot contain a symbolic link or junction.");
            Directory.CreateDirectory(path);
            RejectReparsePointIfPresent(path, "A private Browser profile cannot be a symbolic link or junction.");
            return path;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs cleanup asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task CleanupAsync(Guid tabId, CancellationToken cancellationToken)
    {
        var path = GetProfileDirectory(tabId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await QuarantineAndDeleteAsync(path, cancellationToken).ConfigureAwait(false);
            await DeletePendingTombstonesAsync(
                cancellationToken,
                bestEffort: true).ConfigureAwait(false);
            DeleteRootIfEmpty();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs cleanup orphans asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<int> CleanupOrphansAsync(IReadOnlySet<Guid> activeTabIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activeTabIds);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(_root)) return 0;
            RejectReparsePointsInExistingPath(
                _root,
                "The private Browser profile root path cannot contain a symbolic link or junction.");

            var removed = 0;
            foreach (var directory in Directory.EnumerateDirectories(_root, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var contained = EnsureContained(directory);
                var name = Path.GetFileName(contained);
                if (name.StartsWith(DeletingPrefix, StringComparison.Ordinal))
                {
                    await DeleteDirectoryIfPresentAsync(contained, cancellationToken).ConfigureAwait(false);
                    removed++;
                    continue;
                }

                if (Guid.TryParseExact(name, "N", out var id) && activeTabIds.Contains(id)) continue;
                await QuarantineAndDeleteAsync(contained, cancellationToken).ConfigureAwait(false);
                removed++;
            }

            await DeletePendingTombstonesAsync(
                cancellationToken,
                bestEffort: false).ConfigureAwait(false);
            DeleteRootIfEmpty();
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs quarantine and delete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task QuarantineAndDeleteAsync(string path, CancellationToken cancellationToken)
    {
        path = EnsureContained(path);
        if (!Directory.Exists(path)) return;
        cancellationToken.ThrowIfCancellationRequested();

        var tombstone = EnsureContained(Path.Combine(
            _root,
            $"{DeletingPrefix}{Path.GetFileName(path)}-{Guid.NewGuid():N}"));
        Directory.Move(path, tombstone);

        try
        {
            await DeleteDirectoryIfPresentAsync(tombstone, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The tombstone deliberately remains outside the active profile namespace.
            // Startup/orphan cleanup retries it without making the old profile active again.
            throw;
        }
        catch
        {
            // Preserve the quarantined directory for a later bounded cleanup attempt.
            throw;
        }
    }

    /// <summary>
    /// Performs delete pending tombstones asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DeletePendingTombstonesAsync(
        CancellationToken cancellationToken,
        bool bestEffort)
    {
        if (!Directory.Exists(_root)) return;
        foreach (var tombstone in Directory.EnumerateDirectories(
                     _root,
                     DeletingPrefix + "*",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DeleteDirectoryIfPresentAsync(
                    EnsureContained(tombstone),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex) when (bestEffort)
            {
                System.Diagnostics.Debug.WriteLine(
                    "Deferred private Browser tombstone cleanup: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Performs the delete root if empty step owned by this component.
    /// </summary>
    private void DeleteRootIfEmpty()
    {
        try
        {
            if (Directory.Exists(_root) && !Directory.EnumerateFileSystemEntries(_root).Any())
                Directory.Delete(_root, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A native host or concurrent cleanup may still hold the now-empty root.
            // Future orphan cleanup will retry it.
        }
    }

    /// <summary>
    /// Performs the ensure contained step owned by this component.
    /// </summary>
    private string EnsureContained(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A private Browser profile path escaped the managed profile root.");
        return full;
    }

    /// <summary>
    /// Performs delete directory if present asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task DeleteDirectoryIfPresentAsync(string path, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path)) return;
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(path, false);
                    return;
                }

                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = false,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };
                foreach (var file in Directory.EnumerateFiles(path, "*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { File.SetAttributes(file, FileAttributes.Normal); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                Directory.Delete(path, true);
                return;
            }
            catch (DirectoryNotFoundException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastFailure = ex;
                if (attempt == 3) break;
                await Task.Delay(CleanupRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        throw new IOException(
            "The private Browser profile remained in use after the native host was released.",
            lastFailure);
    }

    /// <summary>
    /// Performs the reject reparse points in existing path step owned by this component.
    /// </summary>
    private static void RejectReparsePointsInExistingPath(string path, string message)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            RejectReparsePointIfPresent(current, message);
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent)
                || parent.Equals(current, StringComparison.OrdinalIgnoreCase)) break;
            current = parent;
        }
    }

    /// <summary>
    /// Performs the reject reparse point if present step owned by this component.
    /// </summary>
    private static void RejectReparsePointIfPresent(string path, string message)
    {
        if (!Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(message);
    }
}
