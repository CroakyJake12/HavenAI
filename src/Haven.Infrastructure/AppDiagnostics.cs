using Haven.Application;

namespace Haven.Infrastructure;

public sealed class DiagnosticEntry
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

public sealed class ErrorRecord
{
    public string Id { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
}

public sealed class AppDiagnostics : IAppDiagnostics
{
    private readonly List<DiagnosticEntry> _entries = new();
    private readonly List<ErrorRecord> _errors = new();
    private readonly object _lock = new();
    private readonly string _correlationPrefix = Guid.NewGuid().ToString("N")[..8];
    private int _sequence;

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
