/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/CallCompletionControllerTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns CallCompletionControllerTests, StubCoordinator, MemoryConversationRepository, RecordingModelClient, RecordingDiagnostics. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents call completion controller tests and keeps its related state and behavior together.
/// </summary>
public sealed class CallCompletionControllerTests
{
    /// <summary>
    /// Stores private frame locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const string PrivateFrame = "PRIVATE_FRAME_BASE64";

    /// <summary>
    /// Performs the repeated completion creates one summary and never sends raw frame metadata step owned by this component.
    /// </summary>
    [Fact]
    public async Task RepeatedCompletionCreatesOneSummaryAndNeverSendsRawFrameMetadata()
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(
            Guid.NewGuid(), HavenMode.Chat, ConversationKind.Call, "Call", null, null,
            false, false, now, now);
        var session = new CallSession(
            Guid.NewGuid(), conversation.Id, "qwen-test", "mic", "default", "voice",
            CallInputMode.HandsFree, true, CallSessionStatus.Completed, now.AddMinutes(-3), now);
        var repository = new MemoryConversationRepository(conversation);
        repository.Messages.Add(new ChatMessage(
            Guid.NewGuid(), conversation.Id, MessageRole.User, "Please check the plan.",
            "You", null, $"{{\"screenFrame\":\"{PrivateFrame}\"}}", now.AddMinutes(-2)));
        repository.Messages.Add(new ChatMessage(
            Guid.NewGuid(), conversation.Id, MessageRole.Assistant, "The plan needs one follow-up action.",
            "Haven", "qwen-test", null, now.AddMinutes(-1)));
        var model = new RecordingModelClient("- Decision: review the plan.\n- Follow-up: assign the action.");
        var coordinator = new StubCoordinator(session, conversation);
        var diagnostics = new RecordingDiagnostics();
        await using var controller = new CallCompletionController(coordinator, repository, model, diagnostics);

        await Task.WhenAll(
            controller.PersistCompletedSessionAsync(session, conversation),
            controller.PersistCompletedSessionAsync(session, conversation));

        var summaries = repository.Messages.Where(message =>
            message.Role == MessageRole.System
            && message.MetadataJson?.Contains("\"summary\":true", StringComparison.Ordinal) == true).ToArray();
        var summary = Assert.Single(summaries);
        Assert.Equal(1, model.CompleteCount);
        Assert.DoesNotContain(PrivateFrame, model.LastRequest?.Messages.Single().Content ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateFrame, summary.Content, StringComparison.Ordinal);
        Assert.Contains("follow-up", summary.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(diagnostics.Events, item => item.EventName == "summary-persisted");
    }

    /// <summary>
    /// Represents stub coordinator and keeps its related state and behavior together.
    /// </summary>
    private sealed class StubCoordinator(CallSession session, Conversation conversation) : ICallCoordinator
    {
        /// <summary>
        /// Gets or updates state, the bindable or domain state represented by this property.
        /// </summary>
        public CallState State => CallState.Idle;
        /// <summary>
        /// Gets or updates current session, the bindable or domain state represented by this property.
        /// </summary>
        public CallSession? CurrentSession => session;
        /// <summary>
        /// Gets or updates current conversation, the bindable or domain state represented by this property.
        /// </summary>
        public Conversation? CurrentConversation => conversation;
        /// <summary>
        /// Gets or updates capabilities, the bindable or domain state represented by this property.
        /// </summary>
        public CallCapabilities Capabilities { get; } = new(false, false, false, null, null, null, [], [], []);
        /// <summary>
        /// Reports whether is active is true for the current state.
        /// </summary>
        public bool IsActive => false;
        /// <summary>
        /// Reports whether is muted is true for the current state.
        /// </summary>
        public bool IsMuted => false;
        /// <summary>
        /// Reports whether is screen sharing is true for the current state.
        /// </summary>
        public bool IsScreenSharing => false;
        /// <summary>
        /// Gets or updates state changed, the bindable or domain state represented by this property.
        /// </summary>
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
        /// <summary>
        /// Gets or updates transcript changed, the bindable or domain state represented by this property.
        /// </summary>
        public event EventHandler<CallTranscriptEventArgs>? TranscriptChanged { add { } remove { } }
        /// <summary>
        /// Gets or updates audio level changed, the bindable or domain state represented by this property.
        /// </summary>
        public event EventHandler<CallAudioLevelEventArgs>? AudioLevelChanged { add { } remove { } }
        /// <summary>
        /// Gets or updates screen preview changed, the bindable or domain state represented by this property.
        /// </summary>
        public event EventHandler<ScreenShareSnapshotEventArgs>? ScreenPreviewChanged { add { } remove { } }
        /// <summary>
        /// Performs start async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<CallSession> StartAsync(CallStartOptions options, SpeechModelInfo? speechModel, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <summary>
        /// Performs submit text async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task SubmitTextAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs begin push to talk async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task BeginPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs end push to talk async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task EndPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs set muted async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task SetMutedAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs pause async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs resume async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs start screen share async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task StartScreenShareAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs stop screen share async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task StopScreenShareAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs interrupt async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task InterruptAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs end async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task EndAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs dispose async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Represents memory conversation repository and keeps its related state and behavior together.
    /// </summary>
    private sealed class MemoryConversationRepository(Conversation conversation) : IConversationRepository
    {
        /// <summary>
        /// Gets or updates conversation, the bindable or domain state represented by this property.
        /// </summary>
        public Conversation Conversation { get; private set; } = conversation;
        /// <summary>
        /// Gets or updates messages, the bindable or domain state represented by this property.
        /// </summary>
        public List<ChatMessage> Messages { get; } = [];
        /// <summary>
        /// Retrieves recent async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Conversation>>([Conversation]);
        /// <summary>
        /// Retrieves async for the current operation.
        /// </summary>
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Conversation?>(id == Conversation.Id ? Conversation : null);
        /// <summary>
        /// Retrieves messages async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(item => item.ConversationId == conversationId).ToArray());
        /// <summary>
        /// Performs upsert conversation async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task UpsertConversationAsync(Conversation value, CancellationToken cancellationToken)
        {
            Conversation = value;
            return Task.CompletedTask;
        }
        /// <summary>
        /// Performs add message async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
        /// <summary>
        /// Performs delete conversation async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Represents recording model client and keeps its related state and behavior together.
    /// </summary>
    private sealed class RecordingModelClient(string response) : IOllamaClient
    {
        /// <summary>
        /// Gets or updates complete count, the bindable or domain state represented by this property.
        /// </summary>
        public int CompleteCount { get; private set; }
        /// <summary>
        /// Gets or updates last request, the bindable or domain state represented by this property.
        /// </summary>
        public OllamaChatRequest? LastRequest { get; private set; }
        /// <summary>
        /// Reports whether is available async is true for the current state.
        /// </summary>
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        /// <summary>
        /// Retrieves models async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        /// <summary>
        /// Performs stream chat async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return response;
        }
        /// <summary>
        /// Performs complete async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            CompleteCount++;
            LastRequest = request;
            return Task.FromResult(response);
        }
        /// <summary>
        /// Performs chat with tools async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(response, []));
    }

    /// <summary>
    /// Represents recording diagnostics and keeps its related state and behavior together.
    /// </summary>
    private sealed class RecordingDiagnostics : IProductionDiagnostics
    {
        /// <summary>
        /// Gets or updates events, the bindable or domain state represented by this property.
        /// </summary>
        public List<ReliabilityEvent> Events { get; } = [];
        /// <summary>
        /// Performs write async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public ValueTask WriteAsync(
            ReliabilitySeverity severity,
            string component,
            string eventName,
            string message,
            IReadOnlyDictionary<string, string>? data = null,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new ReliabilityEvent(
                DateTimeOffset.UtcNow, severity, component, eventName, message,
                correlationId ?? string.Empty, data ?? new Dictionary<string, string>()));
            return ValueTask.CompletedTask;
        }
        /// <summary>
        /// Performs read recent async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReliabilityEvent>>(Events.Take(limit).ToArray());
        /// <summary>
        /// Performs dispose async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
