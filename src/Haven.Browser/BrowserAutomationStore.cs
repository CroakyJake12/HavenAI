using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

public sealed class BrowserAutomationStore : IBrowserAutomationStore, IDisposable
{
    private const long MaximumStoreBytes = 16L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private BrowserAutomationData _data;
    private bool _hasUnsavedRecovery;
    private int _disposed;

    public BrowserAutomationStore(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _path = Path.Combine(paths.DataDirectory, "browser-automation.json");
        _data = Load();
        (_data, _hasUnsavedRecovery) = RecoverAfterStartup(_data, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await PersistRecoveryAndExpiryAsync(cancellationToken).ConfigureAwait(false);
            return _data.Actions.Where(item => item.State == BrowserActionState.Pending)
                .OrderBy(item => item.CreatedAt).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await PersistRecoveryAndExpiryAsync(cancellationToken).ConfigureAwait(false);
            return _data.Audit.OrderByDescending(item => item.RecordedAt).Take(Math.Clamp(limit, 1, 2_000)).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await PersistRecoveryAndExpiryAsync(cancellationToken).ConfigureAwait(false);
            return _data.Downloads.OrderByDescending(item => item.CompletedAt).Take(Math.Clamp(limit, 1, 500)).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<BrowserPendingAction> AddPendingAsync(BrowserPendingAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfDisposed();
        if (action.Id == Guid.Empty || action.State != BrowserActionState.Pending)
            throw new ArgumentException("A new pending browser action requires an identifier and pending state.", nameof(action));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var candidate = ExpirePending(_data, DateTimeOffset.UtcNow, out _);
            if (candidate.Actions.Any(item => item.Id == action.Id))
                throw new InvalidOperationException("The browser action already exists.");
            candidate = candidate with { Actions = candidate.Actions.Append(action).TakeLast(1_000).ToArray() };
            await CommitAsync(candidate, cancellationToken).ConfigureAwait(false);
            return action;
        }
        finally { _gate.Release(); }
    }

    public async Task<BrowserPendingAction?> GetActionAsync(Guid actionId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await PersistRecoveryAndExpiryAsync(cancellationToken).ConfigureAwait(false);
            return _data.Actions.FirstOrDefault(item => item.Id == actionId);
        }
        finally { _gate.Release(); }
    }

    public async Task<BrowserPendingAction> UpdateActionAsync(BrowserPendingAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var candidate = ExpirePending(_data, DateTimeOffset.UtcNow, out _);
            if (!candidate.Actions.Any(item => item.Id == action.Id))
                throw new KeyNotFoundException("The browser action no longer exists.");
            candidate = candidate with
            {
                Actions = candidate.Actions.Select(item => item.Id == action.Id ? action : item).ToArray()
            };
            await CommitAsync(candidate, cancellationToken).ConfigureAwait(false);
            return action;
        }
        finally { _gate.Release(); }
    }

    public async Task AddAuditAsync(BrowserAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var candidate = ExpirePending(_data, DateTimeOffset.UtcNow, out _);
            candidate = candidate with { Audit = candidate.Audit.Append(entry).TakeLast(2_000).ToArray() };
            await CommitAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task AddDownloadAsync(BrowserDownloadRecord download, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(download);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var candidate = ExpirePending(_data, DateTimeOffset.UtcNow, out _);
            candidate = candidate with { Downloads = candidate.Downloads.Append(download).TakeLast(500).ToArray() };
            await CommitAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private BrowserAutomationData Load()
    {
        if (!File.Exists(_path)) return BrowserAutomationData.Empty;
        try
        {
            if (new FileInfo(_path).Length > MaximumStoreBytes)
                throw new InvalidDataException("The browser automation store exceeds its safety limit.");
            return JsonSerializer.Deserialize<BrowserAutomationData>(File.ReadAllText(_path), JsonOptions)
                   ?? BrowserAutomationData.Empty;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or JsonException
                                         or InvalidDataException)
        {
            try
            {
                var quarantine = _path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + ".json";
                File.Move(_path, quarantine, true);
            }
            catch (Exception moveException) when (moveException is IOException or UnauthorizedAccessException) { }
            return BrowserAutomationData.Empty;
        }
    }

    private static (BrowserAutomationData Data, bool Changed) RecoverAfterStartup(
        BrowserAutomationData data,
        DateTimeOffset now)
    {
        var changed = false;
        var recoveryAudit = new List<BrowserAuditEntry>();
        var actions = data.Actions.Select(item =>
        {
            if (item.State == BrowserActionState.Approved)
            {
                changed = true;
                const string failure = "Haven restarted after approval. The action was not resumed automatically.";
                recoveryAudit.Add(new BrowserAuditEntry(
                    Guid.NewGuid(), item.Kind, "recovery-interrupted", item.Origin, failure, false, now));
                return item with { State = BrowserActionState.Failed, UpdatedAt = now, Failure = failure };
            }
            if (item.State == BrowserActionState.Pending && item.Target.StartsWith("ephemeral:", StringComparison.Ordinal))
            {
                changed = true;
                const string unavailable = "The signed request target was session-only and cannot be resumed after restart.";
                recoveryAudit.Add(new BrowserAuditEntry(
                    Guid.NewGuid(), item.Kind, "recovery-session-target-lost", item.Origin, unavailable, false, now));
                return item with { State = BrowserActionState.Failed, UpdatedAt = now, Failure = unavailable };
            }
            if (item.State != BrowserActionState.Pending || item.ExpiresAt > now) return item;
            changed = true;
            const string expired = "The approval expired before execution.";
            recoveryAudit.Add(new BrowserAuditEntry(
                Guid.NewGuid(), item.Kind, "approval-expired", item.Origin, expired, false, now));
            return item with { State = BrowserActionState.Expired, UpdatedAt = now, Failure = expired };
        }).ToArray();
        if (!changed) return (data, false);
        return (data with
        {
            Actions = actions,
            Audit = data.Audit.Concat(recoveryAudit).TakeLast(2_000).ToArray()
        }, true);
    }

    private static BrowserAutomationData ExpirePending(
        BrowserAutomationData data,
        DateTimeOffset now,
        out bool changed)
    {
        changed = false;
        var expirationAudit = new List<BrowserAuditEntry>();
        var actions = data.Actions.Select(item =>
        {
            if (item.State != BrowserActionState.Pending || item.ExpiresAt > now) return item;
            changed = true;
            const string expired = "The approval expired before execution.";
            expirationAudit.Add(new BrowserAuditEntry(
                Guid.NewGuid(), item.Kind, "approval-expired", item.Origin, expired, false, now));
            return item with { State = BrowserActionState.Expired, UpdatedAt = now, Failure = expired };
        }).ToArray();
        if (!changed) return data;
        return data with
        {
            Actions = actions,
            Audit = data.Audit.Concat(expirationAudit).TakeLast(2_000).ToArray()
        };
    }

    private async Task PersistRecoveryAndExpiryAsync(CancellationToken cancellationToken)
    {
        var candidate = ExpirePending(_data, DateTimeOffset.UtcNow, out var expired);
        if (!_hasUnsavedRecovery && !expired) return;
        await CommitAsync(candidate, cancellationToken).ConfigureAwait(false);
    }

    private async Task CommitAsync(BrowserAutomationData candidate, CancellationToken cancellationToken)
    {
        await SaveUnsafeAsync(candidate, cancellationToken).ConfigureAwait(false);
        _data = candidate;
        _hasUnsavedRecovery = false;
    }

    private async Task SaveUnsafeAsync(BrowserAutomationData data, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(data, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        // The store is app-lifetime. Avoid disposing the semaphore underneath an
        // in-flight save during coordinated service-provider shutdown.
    }

    private sealed record BrowserAutomationData(
        IReadOnlyList<BrowserPendingAction> Actions,
        IReadOnlyList<BrowserAuditEntry> Audit,
        IReadOnlyList<BrowserDownloadRecord> Downloads)
    {
        public static BrowserAutomationData Empty { get; } = new([], [], []);
    }
}
