namespace Haven.Application;

/// <summary>
/// Privacy-safe milestones for measuring perceived and end-to-end chat latency.
/// No prompt text, response text, file content, paths, model names, or secrets are stored.
/// </summary>
public enum ChatPerformanceMilestone
{
    SendClicked,
    ComposerCleared,
    UserBubbleRendered,
    UserMessagePersisted,
    ModelSelectionStarted,
    ModelSelectionCompleted,
    ContextAssemblyStarted,
    ContextAssemblyCompleted,
    PromptAssemblyStarted,
    PromptAssemblyCompleted,
    ToolSelectionStarted,
    ToolSelectionCompleted,
    ProviderRequestStarted,
    FirstByteReceived,
    FirstTokenReceived,
    CompletionReceived,
    CompletionPersisted
}

/// <summary>
/// Scalar-only dimensions used to correlate latency without retaining user content.
/// </summary>
public sealed record ChatPerformanceDimensions(
    bool? IsWarmModel = null,
    int? ToolSchemaBytes = null,
    bool? Streaming = null,
    int? ToolCount = null,
    int? ContextCharacterCount = null,
    int? ContextTokenEstimate = null)
{
    public ChatPerformanceDimensions Validate()
    {
        ValidateNonNegative(ToolSchemaBytes, nameof(ToolSchemaBytes));
        ValidateNonNegative(ToolCount, nameof(ToolCount));
        ValidateNonNegative(ContextCharacterCount, nameof(ContextCharacterCount));
        ValidateNonNegative(ContextTokenEstimate, nameof(ContextTokenEstimate));
        return this;
    }

    private static void ValidateNonNegative(int? value, string parameterName)
    {
        if (value is < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Performance dimensions cannot be negative.");
        }
    }
}

/// <summary>One immutable point in a chat performance trace.</summary>
public sealed record ChatPerformanceMark(
    ChatPerformanceMilestone Milestone,
    DateTimeOffset Timestamp,
    TimeSpan Elapsed,
    ChatPerformanceDimensions Dimensions);

/// <summary>
/// Captures the first occurrence of each chat milestone. The trace is thread-safe and stores only
/// timings and bounded scalar dimensions.
/// </summary>
public sealed class ChatPerformanceTrace
{
    private readonly object _gate = new();
    private readonly DateTimeOffset _startedAt;
    private readonly Dictionary<ChatPerformanceMilestone, ChatPerformanceMark> _marks = new();

    public ChatPerformanceTrace(Guid operationId, DateTimeOffset? startedAt = null)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException(
                "A performance trace requires a non-empty operation id.",
                nameof(operationId));
        }

        OperationId = operationId;
        _startedAt = startedAt ?? DateTimeOffset.UtcNow;
    }

    public Guid OperationId { get; }

    public DateTimeOffset StartedAt => _startedAt;

    public IReadOnlyList<ChatPerformanceMark> Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _marks.Values
                    .OrderBy(mark => mark.Timestamp)
                    .ThenBy(mark => mark.Milestone)
                    .ToArray();
            }
        }
    }

    public bool TryMark(
        ChatPerformanceMilestone milestone,
        ChatPerformanceDimensions? dimensions = null,
        DateTimeOffset? timestamp = null)
    {
        dimensions = (dimensions ?? new ChatPerformanceDimensions()).Validate();
        var occurredAt = timestamp ?? DateTimeOffset.UtcNow;

        if (occurredAt < _startedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestamp),
                occurredAt,
                "A milestone cannot occur before the trace starts.");
        }

        lock (_gate)
        {
            if (_marks.ContainsKey(milestone))
            {
                return false;
            }

            _marks.Add(
                milestone,
                new ChatPerformanceMark(
                    milestone,
                    occurredAt,
                    occurredAt - _startedAt,
                    dimensions));
            return true;
        }
    }

    public ChatPerformanceMark? Get(ChatPerformanceMilestone milestone)
    {
        lock (_gate)
        {
            return _marks.TryGetValue(milestone, out var mark) ? mark : null;
        }
    }

    public TimeSpan? DurationBetween(
        ChatPerformanceMilestone start,
        ChatPerformanceMilestone end)
    {
        lock (_gate)
        {
            if (!_marks.TryGetValue(start, out var startMark) ||
                !_marks.TryGetValue(end, out var endMark) ||
                endMark.Timestamp < startMark.Timestamp)
            {
                return null;
            }

            return endMark.Timestamp - startMark.Timestamp;
        }
    }
}
