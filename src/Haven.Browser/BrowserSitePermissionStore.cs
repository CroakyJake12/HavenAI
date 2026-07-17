using System.Text.Json;
using Haven.Application;

namespace Haven.Browser;

public enum BrowserSitePermissionKind
{
    Camera,
    Microphone,
    Geolocation,
    Notifications,
    ClipboardRead,
    MultipleAutomaticDownloads,
    FileReadWrite,
    Autoplay,
    LocalFonts,
    MidiSystemExclusiveMessages,
    WindowManagement
}

public enum BrowserSitePermissionDecision
{
    Ask,
    Allow,
    Deny
}

public sealed record BrowserSitePermission(
    string Origin,
    BrowserSitePermissionKind Kind,
    BrowserSitePermissionDecision Decision,
    DateTimeOffset UpdatedAt);

public sealed record BrowserSitePermissionAudit(
    string Origin,
    BrowserSitePermissionKind Kind,
    BrowserSitePermissionDecision PreviousDecision,
    BrowserSitePermissionDecision Decision,
    DateTimeOffset RecordedAt);

public sealed class BrowserSitePermissionStore : IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumPermissions = 500;
    private const int MaximumAuditEntries = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly string _backupPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PermissionData _data;
    private bool _disposed;

    public BrowserSitePermissionStore(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _path = Path.Combine(paths.DataDirectory, "browser-site-permissions.json");
        _backupPath = _path + ".bak";
        _data = Load();
    }

    public IReadOnlyList<BrowserSitePermission> Permissions => _data.Permissions
        .OrderBy(item => item.Origin, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.Kind)
        .ToArray();

    public IReadOnlyList<BrowserSitePermissionAudit> Audit => _data.Audit
        .OrderByDescending(item => item.RecordedAt)
        .ToArray();

    public BrowserSitePermissionDecision GetDecision(Uri origin, BrowserSitePermissionKind kind)
    {
        var canonical = CanonicalOrigin(origin);
        return _data.Permissions.FirstOrDefault(item =>
                   item.Origin.Equals(canonical, StringComparison.OrdinalIgnoreCase) && item.Kind == kind)?.Decision
               ?? BrowserSitePermissionDecision.Ask;
    }

    public Task SetDecisionAsync(Uri origin, BrowserSitePermissionKind kind, BrowserSitePermissionDecision decision, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(decision)) throw new ArgumentOutOfRangeException(nameof(decision));
        var canonical = CanonicalOrigin(origin);

        return MutateAndSaveAsync(data =>
        {
            var previous = data.Permissions.FirstOrDefault(item =>
                item.Origin.Equals(canonical, StringComparison.OrdinalIgnoreCase) && item.Kind == kind)?.Decision
                ?? BrowserSitePermissionDecision.Ask;
            var permissions = data.Permissions
                .Where(item => !(item.Origin.Equals(canonical, StringComparison.OrdinalIgnoreCase) && item.Kind == kind))
                .ToList();
            if (decision != BrowserSitePermissionDecision.Ask)
                permissions.Add(new BrowserSitePermission(canonical, kind, decision, DateTimeOffset.UtcNow));

            var audit = data.Audit.Prepend(new BrowserSitePermissionAudit(
                    canonical, kind, previous, decision, DateTimeOffset.UtcNow))
                .Take(MaximumAuditEntries)
                .ToArray();
            return data with { Permissions = permissions.TakeLast(MaximumPermissions).ToArray(), Audit = audit };
        }, cancellationToken);
    }

    public Task RevokeOriginAsync(Uri origin, CancellationToken cancellationToken)
    {
        var canonical = CanonicalOrigin(origin);
        return MutateAndSaveAsync(data =>
        {
            var removed = data.Permissions.Where(item => item.Origin.Equals(canonical, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (removed.Length == 0) return data;
            var audit = removed.Select(item => new BrowserSitePermissionAudit(
                    canonical, item.Kind, item.Decision, BrowserSitePermissionDecision.Ask, DateTimeOffset.UtcNow))
                .Concat(data.Audit)
                .Take(MaximumAuditEntries)
                .ToArray();
            return data with
            {
                Permissions = data.Permissions.Where(item => !item.Origin.Equals(canonical, StringComparison.OrdinalIgnoreCase)).ToArray(),
                Audit = audit
            };
        }, cancellationToken);
    }

    public static string CanonicalOrigin(Uri origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!origin.IsAbsoluteUri || origin.Scheme is not ("http" or "https"))
            throw new ArgumentException("Site permission origin must be an absolute HTTP or HTTPS URI.", nameof(origin));
        if (!string.IsNullOrEmpty(origin.UserInfo))
            throw new ArgumentException("Site permission origins cannot contain embedded credentials.", nameof(origin));
        return origin.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private PermissionData Load()
    {
        if (TryLoad(_path, out var primary)) return primary;
        QuarantineInvalidPrimary();
        if (TryLoad(_backupPath, out var backup)) return backup;
        return PermissionData.Empty;
    }

    private static PermissionData Normalize(PermissionData data)
    {
        if (data.SchemaVersion > CurrentSchemaVersion)
            throw new JsonException($"Browser permission schema {data.SchemaVersion} is newer than supported schema {CurrentSchemaVersion}.");

        var permissions = (data.Permissions ?? [])
            .Where(item => Enum.IsDefined(item.Kind) && Enum.IsDefined(item.Decision) && item.Decision != BrowserSitePermissionDecision.Ask)
            .Select(item => item with { Origin = CanonicalOrigin(new Uri(item.Origin, UriKind.Absolute)) })
            .GroupBy(item => (item.Origin.ToUpperInvariant(), item.Kind))
            .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
            .OrderByDescending(item => item.UpdatedAt)
            .Take(MaximumPermissions)
            .ToArray();
        return new PermissionData(CurrentSchemaVersion, permissions, (data.Audit ?? []).Take(MaximumAuditEntries).ToArray());
    }

    private bool TryLoad(string path, out PermissionData data)
    {
        data = PermissionData.Empty;
        if (!File.Exists(path)) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<PermissionData>(File.ReadAllText(path), JsonOptions);
            if (parsed is null) return false;
            data = Normalize(parsed);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or UriFormatException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private void QuarantineInvalidPrimary()
    {
        if (!File.Exists(_path)) return;
        try { File.Move(_path, _path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"), false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private async Task MutateAndSaveAsync(Func<PermissionData, PermissionData> mutation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var original = _data;
            var candidate = Normalize(mutation(original));
            try
            {
                await SaveCoreAsync(candidate, cancellationToken).ConfigureAwait(false);
                _data = candidate;
            }
            catch
            {
                _data = original;
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task SaveCoreAsync(PermissionData candidate, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(candidate, JsonOptions), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_path)) File.Replace(temporary, _path, _backupPath, true);
            else File.Move(temporary, _path, false);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }

    private sealed record PermissionData(
        int SchemaVersion,
        IReadOnlyList<BrowserSitePermission>? Permissions,
        IReadOnlyList<BrowserSitePermissionAudit>? Audit)
    {
        public static PermissionData Empty { get; } = new(CurrentSchemaVersion, [], []);
    }
}
