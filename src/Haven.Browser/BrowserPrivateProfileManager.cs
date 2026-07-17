namespace Haven.Browser;

public sealed class BrowserPrivateProfileManager
{
    private const string PrivateDirectoryName = "private-profiles";
    private static readonly TimeSpan CleanupRetryDelay = TimeSpan.FromMilliseconds(125);
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BrowserPrivateProfileManager(string standardProfileDirectory)
    {
        if (string.IsNullOrWhiteSpace(standardProfileDirectory))
            throw new ArgumentException("A standard Browser profile directory is required.", nameof(standardProfileDirectory));

        var standard = Path.GetFullPath(standardProfileDirectory);
        _root = Path.Combine(standard, PrivateDirectoryName);
    }

    public string RootDirectory => _root;

    public string GetProfileDirectory(Guid tabId)
    {
        if (tabId == Guid.Empty) throw new ArgumentException("A private tab ID is required.", nameof(tabId));
        return Path.Combine(_root, tabId.ToString("N"));
    }

    public async Task<string> CreateAsync(Guid tabId, CancellationToken cancellationToken)
    {
        var path = GetProfileDirectory(tabId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(path);
            return path;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CleanupAsync(Guid tabId, CancellationToken cancellationToken)
    {
        var path = GetProfileDirectory(tabId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DeleteDirectoryIfPresentAsync(path, cancellationToken).ConfigureAwait(false);
            DeleteRootIfEmpty();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> CleanupOrphansAsync(IReadOnlySet<Guid> activeTabIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activeTabIds);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(_root)) return 0;

            var removed = 0;
            foreach (var directory in Directory.EnumerateDirectories(_root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(directory);
                if (Guid.TryParseExact(name, "N", out var id) && activeTabIds.Contains(id)) continue;
                await DeleteDirectoryIfPresentAsync(directory, cancellationToken).ConfigureAwait(false);
                removed++;
            }

            DeleteRootIfEmpty();
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void DeleteRootIfEmpty()
    {
        if (Directory.Exists(_root) && !Directory.EnumerateFileSystemEntries(_root).Any())
            Directory.Delete(_root, false);
    }

    private static async Task DeleteDirectoryIfPresentAsync(string path, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path)) return;
        IOException? lastFailure = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                Directory.Delete(path, true);
                return;
            }
            catch (DirectoryNotFoundException) { return; }
            catch (IOException ex)
            {
                lastFailure = ex;
                if (attempt == 3) break;
                await Task.Delay(CleanupRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        throw new IOException("The private Browser profile remained in use after the native host was released.", lastFailure);
    }
}
