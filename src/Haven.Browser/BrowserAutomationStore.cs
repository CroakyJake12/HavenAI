using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

public sealed class BrowserAutomationStore : IBrowserAutomationStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private BrowserAutomationData _data;

    public BrowserAutomationStore(IAppPaths paths)
    {
        _path = Path.Combine(paths.DataDirectory, "browser-automation.json");
        _data = Load();
        ExpirePending(DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var changed = ExpirePending(DateTimeOffset.UtcNow);
            if (changed) await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return _data.Actions.Where(item => item.State == BrowserActionState.Pending)
                .OrderBy(item => item.CreatedAt).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return _data.Audit.OrderByDescending(item => item.RecordedAt).Take(Math.Clamp(limit, 1, 2_000)).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return _data.Downloads.OrderByDescending(item => item.CompletedAt).Take(Math.Clamp(limit, 1, 500)).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<BrowserPendingAction> AddPendingAsync(BrowserPendingAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Id == Guid.Empty || action.State != BrowserActionState.Pending)
            throw new ArgumentException("A new pending browser action requires an identifier and pending state.", nameof(action));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_data.Actions.Any(item => item.Id == action.Id)) throw new InvalidOperationException("The browser action already exists.");
            _data = _data with { Actions = _data.Actions.Append(action).TakeLast(1_000).ToArray() };
            await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return action;
        }
        finally { _gate.Release(); }
    }

    public async Task<BrowserPendingAction?> GetActionAsync(Guid actionId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var changed = ExpirePending(DateTimeOffset.UtcNow);
            if (changed) await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return _data.Actions.FirstOrDefault(item => item.Id == actionId);
        }
        finally { _gate.Release(); }
    }

    public async Task<BrowserPendingAction> UpdateActionAsync(BrowserPendingAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_data.Actions.Any(item => item.Id == action.Id)) throw new KeyNotFoundException("The browser action no longer exists.");
            _data = _data with { Actions = _data.Actions.Select(item => item.Id == action.Id ? action : item).ToArray() };
            await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return action;
        }
        finally { _gate.Release(); }
    }

    public async Task AddAuditAsync(BrowserAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _data = _data with { Audit = _data.Audit.Append(entry).TakeLast(2_000).ToArray() };
            await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task AddDownloadAsync(BrowserDownloadRecord download, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(download);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _data = _data with { Downloads = _data.Downloads.Append(download).TakeLast(500).ToArray() };
            await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private BrowserAutomationData Load()
    {
        if (!File.Exists(_path)) return BrowserAutomationData.Empty;
        try
        {
            return JsonSerializer.Deserialize<BrowserAutomationData>(File.ReadAllText(_path), JsonOptions) ?? BrowserAutomationData.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
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

    private bool ExpirePending(DateTimeOffset now)
    {
        var changed = false;
        _data = _data with
        {
            Actions = _data.Actions.Select(item =>
            {
                if (item.State != BrowserActionState.Pending || item.ExpiresAt > now) return item;
                changed = true;
                return item with { State = BrowserActionState.Expired, UpdatedAt = now, Failure = "The approval expired before execution." };
            }).ToArray()
        };
        return changed;
    }

    private async Task SaveUnsafeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(_data, JsonOptions), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
        }
    }

    public void Dispose() => _gate.Dispose();

    private sealed record BrowserAutomationData(
        IReadOnlyList<BrowserPendingAction> Actions,
        IReadOnlyList<BrowserAuditEntry> Audit,
        IReadOnlyList<BrowserDownloadRecord> Downloads)
    {
        public static BrowserAutomationData Empty { get; } = new([], [], []);
    }
}
