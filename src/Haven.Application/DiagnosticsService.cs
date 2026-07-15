using System.Diagnostics;
using System.Net.NetworkInformation;
using Haven.Core;

namespace Haven.Application;

public sealed class DiagnosticsService
{
    private readonly IAppDiagnostics _diagnostics;
    private readonly IOllamaClient _ollama;
    private readonly IAppPaths _paths;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    public DiagnosticsService(IAppDiagnostics diagnostics, IOllamaClient ollama, IAppPaths paths)
    {
        _diagnostics = diagnostics;
        _ollama = ollama;
        _paths = paths;
    }

    public async Task<DiagnosticsReport> GetReportAsync(CancellationToken cancellationToken)
    {
        var process = Process.GetCurrentProcess();
        var ollamaAvailable = await _ollama.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
        var models = ollamaAvailable ? await _ollama.GetModelsAsync(cancellationToken).ConfigureAwait(false) : [];
        var recentErrors = await _diagnostics.GetRecentErrorsAsync(20, cancellationToken).ConfigureAwait(false);
        var diagnostics = await _diagnostics.GetDiagnosticsAsync(cancellationToken).ConfigureAwait(false);

        return new DiagnosticsReport
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Uptime = _uptime.Elapsed,
            ProcessId = process.Id,
            WorkingSetMB = process.WorkingSet64 / 1024 / 1024,
            ThreadCount = process.Threads.Count,
            OllamaAvailable = ollamaAvailable,
            OllamaEndpoint = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://127.0.0.1:11434/",
            InstalledModels = models.Select(m => m.Name).ToArray(),
            DatabasePath = _paths.DatabasePath,
            DataDirectory = _paths.DataDirectory,
            DatabaseExists = File.Exists(_paths.DatabasePath),
            DatabaseSizeMB = File.Exists(_paths.DatabasePath) ? new FileInfo(_paths.DatabasePath).Length / 1024 / 1024 : 0,
            RecentErrors = recentErrors.Select(e => new ErrorEntry(e.Component, e.Error, e.Timestamp)).ToArray(),
            SystemInfo = diagnostics
        };
    }

    public async Task<DiagnosticsCheckResult> RunHealthCheckAsync(CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        var warnings = new List<string>();

        var ollamaAvailable = await _ollama.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
        if (!ollamaAvailable)
            issues.Add("Ollama is not available. Local AI features will not work.");

        if (!File.Exists(_paths.DatabasePath))
            issues.Add("Database file does not exist.");

        var dbSize = File.Exists(_paths.DatabasePath) ? new FileInfo(_paths.DatabasePath).Length : 0;
        if (dbSize > 500 * 1024 * 1024)
            warnings.Add($"Database is large: {dbSize / 1024 / 1024} MB. Consider compacting.");

        var process = Process.GetCurrentProcess();
        if (process.WorkingSet64 > 1024 * 1024 * 1024)
            warnings.Add($"Memory usage is high: {process.WorkingSet64 / 1024 / 1024} MB.");

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("127.0.0.1", 1000);
            if (reply.Status != IPStatus.Success)
                warnings.Add("Local network loopback is not responding.");
        }
        catch { warnings.Add("Could not verify network connectivity."); }

        return new DiagnosticsCheckResult(
            issues.Count == 0,
            issues,
            warnings,
            DateTimeOffset.UtcNow);
    }
}

public sealed class DiagnosticsReport
{
    public DateTimeOffset GeneratedAt { get; set; }
    public TimeSpan Uptime { get; set; }
    public int ProcessId { get; set; }
    public long WorkingSetMB { get; set; }
    public int ThreadCount { get; set; }
    public bool OllamaAvailable { get; set; }
    public string OllamaEndpoint { get; set; } = string.Empty;
    public IReadOnlyList<string> InstalledModels { get; set; } = [];
    public string DatabasePath { get; set; } = string.Empty;
    public string DataDirectory { get; set; } = string.Empty;
    public bool DatabaseExists { get; set; }
    public long DatabaseSizeMB { get; set; }
    public IReadOnlyList<ErrorEntry> RecentErrors { get; set; } = [];
    public IReadOnlyDictionary<string, string> SystemInfo { get; set; } = new Dictionary<string, string>();
}

public sealed record ErrorEntry(string Component, string Error, DateTimeOffset Timestamp);
public sealed record DiagnosticsCheckResult(bool Healthy, IReadOnlyList<string> Issues, IReadOnlyList<string> Warnings, DateTimeOffset CheckedAt);
