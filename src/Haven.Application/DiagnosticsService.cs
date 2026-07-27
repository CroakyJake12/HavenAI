/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/DiagnosticsService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns DiagnosticsService, DiagnosticsReport, ErrorEntry, DiagnosticsCheckResult. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Diagnostics;
using System.Net.NetworkInformation;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents diagnostics service and keeps its related state and behavior together.
/// </summary>
public sealed class DiagnosticsService
{
    /// <summary>
    /// Stores diagnostics locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAppDiagnostics _diagnostics;
    /// <summary>
    /// Stores ollama locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _ollama;
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAppPaths _paths;
    /// <summary>
    /// Stores uptime locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    public DiagnosticsService(IAppDiagnostics diagnostics, IOllamaClient ollama, IAppPaths paths)
    {
        _diagnostics = diagnostics;
        _ollama = ollama;
        _paths = paths;
    }

    /// <summary>
    /// Retrieves report async for the current operation.
    /// </summary>
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

    /// <summary>
    /// Runs run health check async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
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

/// <summary>
/// Represents diagnostics report and keeps its related state and behavior together.
/// </summary>
public sealed class DiagnosticsReport
{
    /// <summary>
    /// Gets or updates generated at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; set; }
    /// <summary>
    /// Gets or updates uptime, the bindable or domain state represented by this property.
    /// </summary>
    public TimeSpan Uptime { get; set; }
    /// <summary>
    /// Gets or updates process id, the bindable or domain state represented by this property.
    /// </summary>
    public int ProcessId { get; set; }
    /// <summary>
    /// Gets or updates working set mb, the bindable or domain state represented by this property.
    /// </summary>
    public long WorkingSetMB { get; set; }
    /// <summary>
    /// Gets or updates thread count, the bindable or domain state represented by this property.
    /// </summary>
    public int ThreadCount { get; set; }
    /// <summary>
    /// Gets or updates ollama available, the bindable or domain state represented by this property.
    /// </summary>
    public bool OllamaAvailable { get; set; }
    /// <summary>
    /// Gets or updates ollama endpoint, the bindable or domain state represented by this property.
    /// </summary>
    public string OllamaEndpoint { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates installed models, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<string> InstalledModels { get; set; } = [];
    /// <summary>
    /// Gets or updates database path, the bindable or domain state represented by this property.
    /// </summary>
    public string DatabasePath { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates data directory, the bindable or domain state represented by this property.
    /// </summary>
    public string DataDirectory { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates database exists, the bindable or domain state represented by this property.
    /// </summary>
    public bool DatabaseExists { get; set; }
    /// <summary>
    /// Gets or updates database size mb, the bindable or domain state represented by this property.
    /// </summary>
    public long DatabaseSizeMB { get; set; }
    /// <summary>
    /// Gets or updates recent errors, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<ErrorEntry> RecentErrors { get; set; } = [];
    /// <summary>
    /// Gets or updates system info, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyDictionary<string, string> SystemInfo { get; set; } = new Dictionary<string, string>();
}

/// <summary>
/// Represents error entry and keeps its related state and behavior together.
/// </summary>
public sealed record ErrorEntry(string Component, string Error, DateTimeOffset Timestamp);
/// <summary>
/// Represents diagnostics check result and keeps its related state and behavior together.
/// </summary>
public sealed record DiagnosticsCheckResult(bool Healthy, IReadOnlyList<string> Issues, IReadOnlyList<string> Warnings, DateTimeOffset CheckedAt);
