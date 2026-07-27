/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/AppDiagnostics.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns DiagnosticEntry, ErrorRecord, AppDiagnostics. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Represents diagnostic entry and keeps its related state and behavior together.
/// </summary>
public sealed class DiagnosticEntry
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates category, the bindable or domain state represented by this property.
    /// </summary>
    public string Category { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates message, the bindable or domain state represented by this property.
    /// </summary>
    public string Message { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates timestamp, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
    /// <summary>
    /// Gets or updates metadata, the bindable or domain state represented by this property.
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>
/// Represents error record and keeps its related state and behavior together.
/// </summary>
public sealed class ErrorRecord
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates source, the bindable or domain state represented by this property.
    /// </summary>
    public string Source { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates message, the bindable or domain state represented by this property.
    /// </summary>
    public string Message { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates timestamp, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Represents app diagnostics and keeps its related state and behavior together.
/// </summary>
public sealed class AppDiagnostics : IAppDiagnostics
{
    /// <summary>
    /// Stores entries locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly List<DiagnosticEntry> _entries = new();
    /// <summary>
    /// Stores errors locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly List<ErrorRecord> _errors = new();
    /// <summary>
    /// Stores lock locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly object _lock = new();
    /// <summary>
    /// Stores correlation prefix locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _correlationPrefix = Guid.NewGuid().ToString("N")[..8];
    /// <summary>
    /// Stores sequence locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _sequence;

    /// <summary>
    /// Performs the record entry step owned by this component.
    /// </summary>
    public void RecordEntry(string category, string message, Dictionary<string, string>? metadata = null)
    {
        lock (_lock)
        {
            _entries.Add(new DiagnosticEntry
            {
                Id = $"{_correlationPrefix}-{Interlocked.Increment(ref _sequence)}",
                Category = category,
                Message = message,
                Timestamp = DateTimeOffset.UtcNow,
                Metadata = metadata ?? new Dictionary<string, string>()
            });
            if (_entries.Count > 500) _entries.RemoveAt(0);
        }
    }

    /// <summary>
    /// Performs record error asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task RecordErrorAsync(string component, string error, string? detail, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _errors.Add(new ErrorRecord
            {
                Id = $"{_correlationPrefix}-{Interlocked.Increment(ref _sequence)}",
                Source = component,
                Message = error,
                Timestamp = DateTimeOffset.UtcNow
            });
            if (_errors.Count > 200) _errors.RemoveAt(0);
        }
        RecordEntry("Error", $"[{component}] {error}{(detail is not null ? $" - {detail}" : "")}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves diagnostics async for the current operation.
    /// </summary>
    public Task<IReadOnlyDictionary<string, string>> GetDiagnosticsAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var result = new Dictionary<string, string>
            {
                ["processId"] = Environment.ProcessId.ToString(),
                ["uptime"] = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(),
                ["workingSet"] = Environment.WorkingSet.ToString(),
                ["osVersion"] = Environment.OSVersion.ToString(),
                ["clrVersion"] = Environment.Version.ToString(),
                ["threadCount"] = System.Diagnostics.Process.GetCurrentProcess().Threads.Count.ToString(),
                ["entryCount"] = _entries.Count.ToString(),
                ["errorCount"] = _errors.Count.ToString(),
                ["correlationPrefix"] = _correlationPrefix
            };
            return Task.FromResult<IReadOnlyDictionary<string, string>>(result);
        }
    }

    public Task<IReadOnlyList<(string Component, string Error, DateTimeOffset Timestamp)>> GetRecentErrorsAsync(int limit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var result = _errors
                .TakeLast(limit)
                .Reverse()
                .Select(e => (e.Source, e.Message, e.Timestamp))
                .ToArray();
            return Task.FromResult<IReadOnlyList<(string, string, DateTimeOffset)>>(result);
        }
    }

    /// <summary>
    /// Performs the export redacted step owned by this component.
    /// </summary>
    public string ExportRedacted(int lastN = 100)
    {
        lock (_lock)
        {
            var entries = _entries.TakeLast(lastN).Reverse();
            return string.Join("\n", entries.Select(e =>
                $"[{e.Timestamp:HH:mm:ss.fff}] [{e.Category}] {e.Message}"));
        }
    }
}
