using Haven.Core;

namespace Haven.Application;

/// <summary>Tracks coarse response progress without storing prompts, output, file contents, or secrets.</summary>
public sealed class ChatExecutionTracker : IAsyncDisposable
{
    public static readonly TimeSpan VisibilityDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan EtaDelay = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly List<ChatExecutionLogEntry> _log = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<ChatEtaRequest, CancellationToken, Task<string?>>? _etaProvider;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private ChatExecutionStage _stage;
    private string _status;
    private bool _visible;
    private bool _finished;
    private TimeSpan? _eta;

    public ChatExecutionTracker(
        ChatExecutionStage initialStage = ChatExecutionStage.Preparing,
        Func<ChatEtaRequest, CancellationToken, Task<string?>>? etaProvider = null)
    {
        OperationId = Guid.NewGuid();
        _stage = initialStage;
        _status = ChatExecutionStageText.Get(initialStage);
        _etaProvider = etaProvider;
        _log.Add(new(_startedAt, initialStage, _status));
        _ = RunTimersAsync(_lifetime.Token);
    }

    public Guid OperationId { get; }
    public event Action<ChatExecutionSnapshot>? Changed;

    public ChatExecutionSnapshot Snapshot
    {
        get { lock (_gate) return CreateSnapshot(DateTimeOffset.UtcNow); }
    }

    public void Update(ChatExecutionStage stage, string? summary = null, string? detail = null, bool succeeded = true)
    {
        ChatExecutionSnapshot snapshot;
        lock (_gate)
        {
            if (_finished) return;
            _stage = stage;
            _status = string.IsNullOrWhiteSpace(summary) ? ChatExecutionStageText.Get(stage) : summary.Trim();
            _log.Add(new(DateTimeOffset.UtcNow, stage, _status, TrimDetail(detail), succeeded));
            snapshot = CreateSnapshot(DateTimeOffset.UtcNow);
        }
        Changed?.Invoke(snapshot);
    }

    public void Complete(string summary = "Completed") => Finish(ChatExecutionStage.Completed, summary, true);
    public void Fail(string summary = "Failed", string? detail = null) => Finish(ChatExecutionStage.Failed, summary, false, detail);
    public void Cancel() => Finish(ChatExecutionStage.Cancelled, "Cancelled", false);

    private void Finish(ChatExecutionStage stage, string summary, bool succeeded, string? detail = null)
    {
        ChatExecutionSnapshot snapshot;
        lock (_gate)
        {
            if (_finished) return;
            _finished = true;
            _stage = stage;
            _status = summary;
            var now = DateTimeOffset.UtcNow;
            _log.Add(new(now, stage, summary, TrimDetail(detail), succeeded));
            snapshot = CreateSnapshot(now);
        }
        _lifetime.Cancel();
        Changed?.Invoke(snapshot);
    }

    private async Task RunTimersAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(VisibilityDelay, cancellationToken).ConfigureAwait(false);
            PublishVisible();
            await Task.Delay(EtaDelay - VisibilityDelay, cancellationToken).ConfigureAwait(false);
            await RequestEtaAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void PublishVisible()
    {
        ChatExecutionSnapshot? snapshot = null;
        lock (_gate)
        {
            if (!_finished && !_visible)
            {
                _visible = true;
                snapshot = CreateSnapshot(DateTimeOffset.UtcNow);
            }
        }
        if (snapshot is not null) Changed?.Invoke(snapshot);
    }

    private async Task RequestEtaAsync(CancellationToken cancellationToken)
    {
        if (_etaProvider is null) return;
        ChatEtaRequest request;
        lock (_gate)
        {
            if (_finished || _eta is not null) return;
            request = new(OperationId, _stage, _status, DateTimeOffset.UtcNow - _startedAt,
                _log.Select(item => item.Summary).TakeLast(12).ToArray());
        }

        string? answer;
        try { answer = await _etaProvider(request, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        catch { return; }

        if (!ChatEtaFormatter.TryParseClearEstimate(answer, out var estimate)) return;
        ChatExecutionSnapshot snapshot;
        lock (_gate)
        {
            if (_finished) return;
            _eta = estimate;
            snapshot = CreateSnapshot(DateTimeOffset.UtcNow);
        }
        Changed?.Invoke(snapshot);
    }

    private ChatExecutionSnapshot CreateSnapshot(DateTimeOffset now) =>
        new(OperationId, _stage, _status, _startedAt, now, _visible, _eta, _log.ToArray());

    private static string? TrimDetail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= 800 ? trimmed : trimmed[..800] + "…";
    }

    public ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        return default;
    }
}

    public sealed record ChatEtaRequest(
    Guid OperationId,
    ChatExecutionStage Stage,
    string CurrentStatus,
    TimeSpan Elapsed,
    IReadOnlyList<string> RecentActivity);

