using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Haven.Application;

/// <summary>
/// Broad, user-facing stages for long-running responses. Stages intentionally avoid
/// noisy implementation details such as individual file lines.
/// </summary>
public enum ResponseProgressStage
{
    Preparing,
    LoadingModel,
    LoadingContext,
    Thinking,
    InspectingCode,
    RunningCommand,
    UsingBrowser,
    UsingTool,
    WaitingForApproval,
    WritingResponse,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// One meaningful item shown in the expandable response activity log.
/// </summary>
public sealed record ResponseProgressEntry(
    DateTimeOffset Timestamp,
    ResponseProgressStage Stage,
    string Summary,
    string? Detail = null,
    bool Succeeded = true);

/// <summary>
/// Request supplied to an LLM-backed ETA estimator after a task has run for one minute.
/// The request deliberately contains summaries rather than private prompt contents.
/// </summary>
public sealed record ResponseEtaRequest(
    string CurrentStage,
    TimeSpan Elapsed,
    IReadOnlyList<ResponseProgressEntry> RecentActivity);

public interface IResponseEtaEstimator
{
    Task<TimeSpan?> EstimateAsync(
        ResponseEtaRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Tracks accurate, coarse-grained response status for the ChatGPT-style activity indicator.
/// </summary>
public sealed class ResponseProgressTracker
{
    public static readonly TimeSpan DisplayDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan EtaDelay = TimeSpan.FromMinutes(1);

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly ObservableCollection<ResponseProgressEntry> _entries = [];
    private ResponseProgressStage _stage = ResponseProgressStage.Preparing;
    private string _status = "Preparing";
    private TimeSpan? _eta;

    public event EventHandler? Changed;

    public ReadOnlyObservableCollection<ResponseProgressEntry> Entries { get; }

    public ResponseProgressTracker()
    {
        Entries = new ReadOnlyObservableCollection<ResponseProgressEntry>(_entries);
    }

    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public ResponseProgressStage Stage => _stage;
    public string Status => _status;
    public TimeSpan? Eta => _eta;
    public bool ShouldShow => Elapsed >= DisplayDelay && !IsTerminal;
    public bool NeedsEta => Elapsed >= EtaDelay && _eta is null && !IsTerminal;
    public bool IsTerminal => _stage is ResponseProgressStage.Completed
        or ResponseProgressStage.Failed
        or ResponseProgressStage.Cancelled;

    public string DisplayText =>
        _eta is { } eta
            ? $"{_status}. ETA for task: {FormatDuration(eta)}"
            : _status;

    public void Update(
        ResponseProgressStage stage,
        string status,
        string? detail = null,
        bool succeeded = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        _stage = stage;
        _status = status.Trim();
        _entries.Add(new ResponseProgressEntry(
            DateTimeOffset.UtcNow,
            stage,
            _status,
            string.IsNullOrWhiteSpace(detail) ? null : detail.Trim(),
            succeeded));

        const int maximumEntries = 100;
        while (_entries.Count > maximumEntries)
        {
            _entries.RemoveAt(0);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetEta(TimeSpan eta)
    {
        if (eta <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(eta), "ETA must be a positive duration.");
        }

        _eta = eta;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ResponseEtaRequest CreateEtaRequest(int recentEntryCount = 12)
    {
        if (recentEntryCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recentEntryCount));
        }

        var recent = _entries
            .Skip(Math.Max(0, _entries.Count - recentEntryCount))
            .ToArray();

        return new ResponseEtaRequest(_status, Elapsed, recent);
    }

    public void Complete() => Update(ResponseProgressStage.Completed, "Completed");

    public void Fail(string summary, string? detail = null) =>
        Update(ResponseProgressStage.Failed, summary, detail, succeeded: false);

    public void Cancel() =>
        Update(ResponseProgressStage.Cancelled, "Cancelled", succeeded: false);

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            var hours = Math.Max(1, (int)Math.Round(duration.TotalHours));
            return hours == 1 ? "1 hour" : $"{hours} hours";
        }

        var minutes = Math.Max(1, (int)Math.Round(duration.TotalMinutes));
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }
}