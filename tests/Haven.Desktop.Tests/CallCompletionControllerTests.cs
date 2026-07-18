using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class CallCompletionControllerTests
{
    private const string PrivateFrame = "PRIVATE_FRAME_BASE64";

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

    private sealed class StubCoordinator(CallSession session, Conversation conversation) : ICallCoordinator
    {
        public CallState State => CallState.Idle;
        public CallSession? CurrentSession => session;
        public Conversation? CurrentConversation => conversation;
        public CallCapabilities Capabilities { get; } = new(false, false, false, null, null, null, [], [], []);
        public bool IsActive => false;
        public bool IsMuted => false;
        public bool IsScreenSharing => false;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<CallTranscriptEventArgs>? TranscriptChanged { add { } remove { } }
        public event EventHandler<CallAudioLevelEventArgs>? AudioLevelChanged { add { } remove { } }
        public event EventHandler<ScreenShareSnapshotEventArgs>? ScreenPreviewChanged { add { } remove { } }
        public Task<CallSession> StartAsync(CallStartOptions options, SpeechModelInfo? speechModel, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SubmitTextAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task BeginPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EndPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetMutedAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartScreenShareAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopScreenShareAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InterruptAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EndAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MemoryConversationRepository(Conversation conversation) : IConversationRepository
    {
        public Conversation Conversation { get; private set; } = conversation;
        public List<ChatMessage> Messages { get; } = [];
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Conversation>>([Conversation]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Conversation?>(id == Conversation.Id ? Conversation : null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(item => item.ConversationId == conversationId).ToArray());
        public Task UpsertConversationAsync(Conversation value, CancellationToken cancellationToken)
        {
            Conversation = value;
            return Task.CompletedTask;
        }
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingModelClient(string response) : IOllamaClient
    {
        public int CompleteCount { get; private set; }
        public OllamaChatRequest? LastRequest { get; private set; }
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return response;
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            CompleteCount++;
            LastRequest = request;
            return Task.FromResult(response);
        }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(response, []));
    }

    private sealed class RecordingDiagnostics : IProductionDiagnostics
    {
        public List<ReliabilityEvent> Events { get; } = [];
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
        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReliabilityEvent>>(Events.Take(limit).ToArray());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
