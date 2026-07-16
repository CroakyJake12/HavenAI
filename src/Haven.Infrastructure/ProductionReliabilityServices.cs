using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed class ProductionDiagnostics(IAppPaths paths) : IProductionDiagnostics
{
    private const long MaximumFileBytes = 5L * 1024 * 1024;
    private const int MaximumFiles = 20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex SensitiveKeyPattern = new(
        "(?:api[-_]?key|access[-_]?token|refresh[-_]?token|secret|password|authorization|cookie|credential|client[-_]?secret)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UrlPattern = new(
        "https?://[^\\s\\\"'<>]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public async ValueTask WriteAsync(
        ReliabilitySeverity severity,
        string component,
        string eventName,
        string message,
        IReadOnlyDictionary<string, string>? data = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var item = new ReliabilityEvent(
            DateTimeOffset.UtcNow,
            severity,
            NormalizeToken(component, 80, "Haven"),
            NormalizeToken(eventName, 120, "event"),
            Redact(message, 8_000),
            NormalizeToken(correlationId, 80, Guid.NewGuid().ToString("N")),
            SanitizeData(data));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            var target = SelectCurrentFile();
            var line = JsonSerializer.Serialize(item, JsonOptions) + Environment.NewLine;
            await using var stream = new FileStream(
                target,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            var bytes = Encoding.UTF8.GetBytes(line);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
            ApplyRetention();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        limit = Math.Clamp(limit, 1, 5_000);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(paths.LogsDirectory)) return [];
            var result = new List<ReliabilityEvent>(Math.Min(limit, 256));
            foreach (var file in EnumerateLogFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] lines;
                try { lines = await File.ReadAllLinesAsync(file, cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                for (var index = lines.Length - 1; index >= 0 && result.Count < limit; index--)
                {
                    if (string.IsNullOrWhiteSpace(lines[index])) continue;
                    try
                    {
                        var item = JsonSerializer.Deserialize<ReliabilityEvent>(lines[index], JsonOptions);
                        if (item is not null) result.Add(item);
                    }
                    catch (JsonException) { }
                }
                if (result.Count >= limit) break;
            }
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    internal static string Redact(string? value, int maximumLength = 4_000)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sanitized = value.Replace('\0', ' ');
        sanitized = UrlPattern.Replace(sanitized, match => RedactUrl(match.Value));
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
            sanitized = sanitized.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        sanitized = Regex.Replace(
            sanitized,
            "(?i)(api[-_]?key|token|secret|password|authorization|cookie|credential)\\s*[:=]\\s*([^\\s,;]+)",
            "$1=<redacted>",
            RegexOptions.CultureInvariant);
        return sanitized.Length <= maximumLength ? sanitized : sanitized[..maximumLength] + "…";
    }

    private IReadOnlyDictionary<string, string> SanitizeData(IReadOnlyDictionary<string, string>? data)
    {
        if (data is null || data.Count == 0) return new Dictionary<string, string>();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in data.Take(64))
        {
            var key = NormalizeToken(pair.Key, 100, "value");
            result[key] = SensitiveKeyPattern.IsMatch(key) ? "<redacted>" : Redact(pair.Value, 2_000);
        }
        return result;
    }

    private string SelectCurrentFile()
    {
        var prefix = "haven-" + DateTime.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        for (var index = 0; index < 100; index++)
        {
            var suffix = index == 0 ? string.Empty : "-" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            var candidate = Path.Combine(paths.LogsDirectory, prefix + suffix + ".jsonl");
            if (!File.Exists(candidate) || new FileInfo(candidate).Length < MaximumFileBytes) return candidate;
        }
        return Path.Combine(paths.LogsDirectory, prefix + "-overflow-" + Guid.NewGuid().ToString("N") + ".jsonl");
    }

    private void ApplyRetention()
    {
        var files = EnumerateLogFiles().ToArray();
        var cutoff = DateTime.UtcNow.AddDays(-14);
        foreach (var file in files.Skip(MaximumFiles).Concat(files.Where(file => File.GetLastWriteTimeUtc(file) < cutoff)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private IEnumerable<string> EnumerateLogFiles() => Directory.EnumerateFiles(paths.LogsDirectory, "haven-*.jsonl", SearchOption.TopDirectoryOnly)
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase);

    private static string NormalizeToken(string? value, int maximumLength, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        normalized = new string(normalized.Where(character => !char.IsControl(character)).ToArray());
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string RedactUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value;
        try
        {
            return new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty, UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri;
        }
        catch (UriFormatException) { return uri.GetLeftPart(UriPartial.Path); }
    }
}

public sealed class StartupRecoveryCoordinator(IAppPaths paths, IProductionDiagnostics diagnostics) : IStartupRecoveryCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(15);
    private const int SafeModeThreshold = 3;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _statePath = Path.Combine(paths.DataDirectory, "startup-recovery.json");

    public StartupRecoveryState Current { get; private set; } = new(false, 0, string.Empty, "Startup recovery has not run.", DateTimeOffset.MinValue);

    public async Task<StartupRecoveryState> BeginStartupAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var persisted = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var failures = persisted.RecentUncleanStarts
                .Where(value => now - value <= CrashWindow && value <= now)
                .OrderBy(value => value)
                .ToList();
            if (persisted.CurrentRun is { CleanShutdown: false } previous && !failures.Contains(previous.StartedAt))
                failures.Add(previous.StartedAt);
            failures = failures.Where(value => now - value <= CrashWindow).Distinct().OrderBy(value => value).ToList();

            var safeMode = failures.Count >= SafeModeThreshold;
            var reason = safeMode
                ? $"Haven detected {failures.Count} unclean starts within {CrashWindow.TotalMinutes:0} minutes. External tools, browser automation, cloud providers and workspace mutations are disabled for this run."
                : failures.Count == 0
                    ? "Normal startup."
                    : $"Recovered after {failures.Count} recent unclean start{(failures.Count == 1 ? string.Empty : "s")}.";
            var run = new StartupRun(Guid.NewGuid().ToString("N"), now, StartupCompleted: false, CleanShutdown: false);
            await WriteAsync(new StartupState(1, run, failures), cancellationToken).ConfigureAwait(false);
            Current = new StartupRecoveryState(safeMode, failures.Count, run.Id, reason, now);
            if (safeMode) RuntimeSafetyState.EnableSafeMode(reason); else RuntimeSafetyState.DisableSafeMode();
            await diagnostics.WriteAsync(
                safeMode ? ReliabilitySeverity.Warning : ReliabilitySeverity.Information,
                "startup",
                "begin",
                reason,
                new Dictionary<string, string>
                {
                    ["runId"] = run.Id,
                    ["recentUncleanStarts"] = failures.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["safeMode"] = safeMode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                },
                run.Id,
                cancellationToken).ConfigureAwait(false);
            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkStartupCompletedAsync(CancellationToken cancellationToken)
    {
        await UpdateCurrentAsync(run => run with { StartupCompleted = true }, "startup-complete", cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkCleanShutdownAsync(CancellationToken cancellationToken)
    {
        await UpdateCurrentAsync(run => run with { StartupCompleted = true, CleanShutdown = true }, "clean-shutdown", cancellationToken).ConfigureAwait(false);
        RuntimeSafetyState.DisableSafeMode();
    }

    private async Task UpdateCurrentAsync(Func<StartupRun, StartupRun> update, string eventName, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var persisted = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (persisted.CurrentRun is null) return;
            var updated = update(persisted.CurrentRun);
            await WriteAsync(persisted with { CurrentRun = updated }, cancellationToken).ConfigureAwait(false);
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "startup",
                eventName,
                eventName == "clean-shutdown" ? "Haven recorded a clean shutdown." : "Haven completed startup.",
                new Dictionary<string, string> { ["runId"] = updated.Id },
                updated.Id,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StartupState> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_statePath)) return new StartupState(1, null, []);
        try
        {
            await using var stream = new FileStream(_statePath, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<StartupState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                   ?? new StartupState(1, null, []);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            var quarantine = _statePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
            try { File.Move(_statePath, quarantine, overwrite: false); }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException) { }
            await diagnostics.WriteAsync(ReliabilitySeverity.Warning, "startup", "state-quarantined", "The startup recovery state was unreadable and has been quarantined.", cancellationToken: cancellationToken).ConfigureAwait(false);
            return new StartupState(1, null, []);
        }
    }

    private async Task WriteAsync(StartupState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var temp = _statePath + ".tmp-" + Guid.NewGuid().ToString("N");
        var backup = _statePath + ".bak";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(_statePath)) File.Replace(temp, _statePath, backup, ignoreMetadataErrors: true);
            else File.Move(temp, _statePath);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record StartupState(int Version, StartupRun? CurrentRun, IReadOnlyList<DateTimeOffset> RecentUncleanStarts);
    private sealed record StartupRun(string Id, DateTimeOffset StartedAt, bool StartupCompleted, bool CleanShutdown);
}

public sealed class DiagnosticsBundleService(
    IAppPaths paths,
    IDatabaseMaintenance database,
    IStartupRecoveryCoordinator startup,
    IProductionDiagnostics diagnostics) : IDiagnosticsBundleService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<string> CreateBundleAsync(string destinationDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory)) throw new ArgumentException("A destination directory is required.", nameof(destinationDirectory));
        var destination = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destination);
        var finalPath = Path.Combine(destination, "haven-diagnostics-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        var tempPath = finalPath + ".tmp";
        try
        {
            var health = await database.VerifyIntegrityAsync(cancellationToken).ConfigureAwait(false);
            var summary = new
            {
                generatedAt = DateTimeOffset.UtcNow,
                operatingSystem = Environment.OSVersion.VersionString,
                framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                safeMode = startup.Current.IsSafeMode,
                safeModeReason = ProductionDiagnostics.Redact(startup.Current.Reason),
                recentUncleanStarts = startup.Current.RecentUncleanStarts,
                database = health,
                note = "This bundle intentionally excludes conversations, prompts, attachments, provider secrets, browser data and settings values."
            };

            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var summaryEntry = archive.CreateEntry("environment.json", CompressionLevel.SmallestSize);
                await using (var entry = summaryEntry.Open())
                    await JsonSerializer.SerializeAsync(entry, summary, JsonOptions, cancellationToken).ConfigureAwait(false);

                if (Directory.Exists(paths.LogsDirectory))
                {
                    foreach (var log in Directory.EnumerateFiles(paths.LogsDirectory, "haven-*.jsonl", SearchOption.TopDirectoryOnly)
                                 .OrderByDescending(File.GetLastWriteTimeUtc)
                                 .Take(20))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entry = archive.CreateEntry("logs/" + Path.GetFileName(log), CompressionLevel.SmallestSize);
                        await using var output = entry.Open();
                        await using var input = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            File.Move(tempPath, finalPath);
            await diagnostics.WriteAsync(ReliabilitySeverity.Information, "diagnostics", "bundle-created", "A redacted diagnostics bundle was created.", new Dictionary<string, string> { ["path"] = finalPath }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return finalPath;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
