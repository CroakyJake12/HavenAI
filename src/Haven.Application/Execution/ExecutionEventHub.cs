using System.Threading.Channels;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Shared, non-blocking execution event stream for Action Graph, live activities,
/// notifications and task status. Graph projection is deliberately performed by readers.
/// </summary>
public sealed class ExecutionEventHub : IExecutionEventSink, IAsyncDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(150);
    private const int MaximumBatchSize = 64;

    private readonly IExecutionEventRepository _repository;
    private readonly Channel<ExecutionEvent> _channel;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _collector;
    private long _queuedFallbacks;
    private long _persistenceFailures;

    public ExecutionEventHub(IExecutionEventRepository repository)
    {
        _repository = repository;
        _channel = Channel.CreateBounded<ExecutionEvent>(new BoundedChannelOptions(8192)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _collector = CollectAsync(_lifetime.Token);
    }

    public event EventHandler<ExecutionEvent>? Published;
    public long QueuedFallbackCount => Interlocked.Read(ref _queuedFallbacks);
    public long PersistenceFailureCount => Interlocked.Read(ref _persistenceFailures);

    public bool TryPublish(ExecutionEvent executionEvent)
    {
        ArgumentNullException.ThrowIfNull(executionEvent);
        var safe = Redact(executionEvent);
        if (Published is { } published)
        {
            foreach (EventHandler<ExecutionEvent> handler in published.GetInvocationList())
            {
                try { handler(this, safe); }
                catch { /* An observer failure must never fail the originating action. */ }
            }
        }
        if (_channel.Writer.TryWrite(safe)) return true;
        Interlocked.Increment(ref _queuedFallbacks);
        _ = EnqueueWithoutBlockingPublisherAsync(safe, _lifetime.Token);
        return false;
    }

    private async Task EnqueueWithoutBlockingPublisherAsync(ExecutionEvent executionEvent, CancellationToken cancellationToken)
    {
        try { await _channel.Writer.WriteAsync(executionEvent, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ChannelClosedException) { }
    }

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        var batch = new List<ExecutionEvent>(MaximumBatchSize);
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (batch.Count < MaximumBatchSize && _channel.Reader.TryRead(out var item)) batch.Add(item);
                if (batch.Count < MaximumBatchSize)
                    await Task.Delay(FlushInterval, cancellationToken).ConfigureAwait(false);
                while (batch.Count < MaximumBatchSize && _channel.Reader.TryRead(out var item)) batch.Add(item);
                if (batch.Count == 0) continue;
                await PersistBatchWithRetryAsync(batch.ToArray(), cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            while (_channel.Reader.TryRead(out var item)) batch.Add(item);
            if (batch.Count > 0)
            {
                try { await _repository.AppendAsync(batch, CancellationToken.None).ConfigureAwait(false); }
                catch { /* Shutdown persistence cannot block process exit. */ }
            }
        }
    }

    private async Task PersistBatchWithRetryAsync(IReadOnlyList<ExecutionEvent> batch, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await _repository.AppendAsync(batch, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                Interlocked.Increment(ref _persistenceFailures);
                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static ExecutionEvent Redact(ExecutionEvent value)
    {
        var metadata = value.SafeMetadata?.ToDictionary(
            pair => SensitiveTextRedactor.Redact(pair.Key, 128),
            pair => SensitiveTextRedactor.Redact(pair.Value, 2_000),
            StringComparer.Ordinal);
        var failure = value.Failure is null ? null : value.Failure with
        {
            Message = SensitiveTextRedactor.Redact(value.Failure.Message, 4_000),
            ProviderMessage = SensitiveTextRedactor.Redact(value.Failure.ProviderMessage, 4_000)
        };
        return value with
        {
            Name = SensitiveTextRedactor.Redact(value.Name, 256),
            SafeReasoningSummary = SensitiveTextRedactor.Redact(value.SafeReasoningSummary, 2_000),
            SafeDetail = SensitiveTextRedactor.Redact(value.SafeDetail, 8_000),
            ComponentId = SensitiveTextRedactor.Redact(value.ComponentId, 256),
            Failure = failure,
            SafeMetadata = metadata
        };
    }

    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _channel.Writer.TryComplete();
        _lifetime.CancelAfter(TimeSpan.FromSeconds(2));
        try { await _collector.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _lifetime.Dispose();
    }
}

public sealed class ExecutionTraceService(
    IExecutionEventRepository events,
    IActionFeedbackRepository feedback)
{
    public Task<IReadOnlyList<ExecutionSummary>> SearchAsync(string? query, int limit, CancellationToken cancellationToken) =>
        events.SearchExecutionsAsync(query, Math.Clamp(limit, 1, 500), cancellationToken);

    public Task<IReadOnlyList<ExecutionEvent>> GetTraceAsync(Guid executionId, CancellationToken cancellationToken) =>
        events.GetExecutionAsync(executionId, cancellationToken);

    public Task UpsertFeedbackAsync(ActionFeedback value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Rating is null && string.IsNullOrWhiteSpace(value.Comment))
            throw new ArgumentException("Feedback requires a rating or comment.", nameof(value));
        var safe = value with
        {
            Comment = SensitiveTextRedactor.Redact(value.Comment, 4_000),
            SafeContext = SensitiveTextRedactor.Redact(value.SafeContext, 2_000),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return feedback.UpsertAsync(safe, cancellationToken);
    }

    public Task DeleteFeedbackAsync(Guid feedbackId, CancellationToken cancellationToken) =>
        feedback.DeleteAsync(feedbackId, cancellationToken);
}
