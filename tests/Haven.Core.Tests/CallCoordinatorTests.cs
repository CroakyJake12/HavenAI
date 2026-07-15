using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class CallCoordinatorTests
{
    [Fact]
    public async Task TypedVisionCallStreamsSpeechAndNeverPersistsFrameData()
    {
        var calls = new MemoryCallRepository();
        var conversations = new MemoryConversationRepository();
        var ollama = new FakeOllamaClient(["Hello there. ", "How can I help?"]);
        var speechInput = new FakeSpeechInput();
        var speechOutput = new FakeSpeechOutput();
        var screen = new FakeScreenShare();
        await using var coordinator = new CallCoordinator(
            calls, conversations, ollama, speechInput, speechOutput, screen);
        var states = new List<CallState>();
        coordinator.StateChanged += (_, args) => states.Add(args.State);

        var session = await coordinator.StartAsync(
            new CallStartOptions(Model(vision: true)),
            null,
            CancellationToken.None);
        await coordinator.StartScreenShareAsync(CancellationToken.None);
        await coordinator.SubmitTextAsync("What is on my screen?", CancellationToken.None);
        await coordinator.EndAsync(CancellationToken.None);

        Assert.Equal(ConversationKind.Call, conversations.Items[session.ConversationId].Kind);
        Assert.Collection(
            conversations.Messages[session.ConversationId],
            user =>
            {
                Assert.Equal(MessageRole.User, user.Role);
                Assert.Equal("What is on my screen?", user.Content);
            },
            assistant =>
            {
                Assert.Equal(MessageRole.Assistant, assistant.Role);
                Assert.Equal("Hello there. How can I help?", assistant.Content);
            });
        Assert.DoesNotContain(
            conversations.Messages[session.ConversationId],
            message => message.Content.Contains(FakeScreenShare.FrameData, StringComparison.Ordinal)
                || (message.MetadataJson?.Contains(FakeScreenShare.FrameData, StringComparison.Ordinal) ?? false));
        Assert.Equal(FakeScreenShare.FrameData, ollama.LastRequest?.Messages.Last().Images?.Single());
        Assert.Equal(["Hello there.", "How can I help?"], speechOutput.Spoken);
        Assert.Contains(CallState.Thinking, states);
        Assert.Contains(CallState.Speaking, states);
        Assert.Equal(CallSessionStatus.Completed, calls.Items[session.Id].Status);
        Assert.True(calls.Items[session.Id].UsedScreenShare);
        Assert.True(screen.StopCount > 0);
    }

    [Fact]
    public async Task CoordinatorRejectsASecondActiveCall()
    {
        await using var coordinator = CreateCoordinator(out _, out _, out _, out _, out _);
        await coordinator.StartAsync(new CallStartOptions(Model()), null, CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAsync(new CallStartOptions(Model()), null, CancellationToken.None));

        Assert.Contains("already active", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InterruptCancelsGenerationAndPersistsOnlyMarkedPartialTranscript()
    {
        var calls = new MemoryCallRepository();
        var conversations = new MemoryConversationRepository();
        var ollama = new FakeOllamaClient(["A partial answer "]) { WaitAfterFirstChunk = true };
        var speechOutput = new FakeSpeechOutput();
        await using var coordinator = new CallCoordinator(
            calls,
            conversations,
            ollama,
            new FakeSpeechInput(),
            speechOutput,
            new FakeScreenShare());
        var session = await coordinator.StartAsync(
            new CallStartOptions(Model()), null, CancellationToken.None);

        var turn = coordinator.SubmitTextAsync("Start talking", CancellationToken.None);
        await ollama.FirstChunk.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.InterruptAsync(CancellationToken.None);
        await turn.WaitAsync(TimeSpan.FromSeconds(2));

        var assistant = conversations.Messages[session.ConversationId].Single(item => item.Role == MessageRole.Assistant);
        Assert.Equal("A partial answer ", assistant.Content);
        Assert.Contains("\"interrupted\":true", assistant.MetadataJson);
        Assert.True(speechOutput.StopCount > 0);
        Assert.Equal(CallState.Listening, coordinator.State);
    }

    [Fact]
    public async Task SpeechSourceClosureFailsCallAndReleasesEveryMediaService()
    {
        var calls = new MemoryCallRepository();
        var input = new FakeSpeechInput();
        var output = new FakeSpeechOutput();
        var screen = new FakeScreenShare();
        await using var coordinator = new CallCoordinator(
            calls,
            new MemoryConversationRepository(),
            new FakeOllamaClient(["unused"]),
            input,
            output,
            screen);
        var session = await coordinator.StartAsync(
            new CallStartOptions(Model()), null, CancellationToken.None);

        await input.EmitAsync(new SpeechInputEvent(SpeechInputEventKind.SourceClosed));

        Assert.False(coordinator.IsActive);
        Assert.Equal(CallState.Error, coordinator.State);
        Assert.Equal(CallSessionStatus.Failed, calls.Items[session.Id].Status);
        Assert.NotNull(calls.Items[session.Id].EndedAt);
        Assert.True(input.StopCount > 0);
        Assert.True(output.StopCount > 0);
        Assert.True(screen.StopCount > 0);
    }

    [Fact]
    public async Task ScreenSourceClosureFailsCallAndReleasesEveryMediaService()
    {
        var calls = new MemoryCallRepository();
        var input = new FakeSpeechInput();
        var output = new FakeSpeechOutput();
        var screen = new FakeScreenShare();
        await using var coordinator = new CallCoordinator(
            calls,
            new MemoryConversationRepository(),
            new FakeOllamaClient(["unused"]),
            input,
            output,
            screen);
        var session = await coordinator.StartAsync(
            new CallStartOptions(Model(vision: true)), null, CancellationToken.None);
        await coordinator.StartScreenShareAsync(CancellationToken.None);

        screen.RaiseSourceClosed();
        await WaitUntilAsync(() => !coordinator.IsActive);

        Assert.Equal(CallState.Error, coordinator.State);
        Assert.Equal(CallSessionStatus.Failed, calls.Items[session.Id].Status);
        Assert.True(input.StopCount > 0);
        Assert.True(output.StopCount > 0);
        Assert.True(screen.StopCount > 0);
    }

    [Fact]
    public async Task NonVisionCallDoesNotReadOrSendCapturedFrames()
    {
        var ollama = new FakeOllamaClient(["Voice only."]);
        var screen = new FakeScreenShare();
        await using var coordinator = new CallCoordinator(
            new MemoryCallRepository(),
            new MemoryConversationRepository(),
            ollama,
            new FakeSpeechInput(),
            new FakeSpeechOutput(),
            screen);
        await coordinator.StartAsync(new CallStartOptions(Model()), null, CancellationToken.None);
        await coordinator.StartScreenShareAsync(CancellationToken.None);

        await coordinator.SubmitTextAsync("Describe this", CancellationToken.None);

        Assert.Equal(0, screen.GetSnapshotCount);
        Assert.Null(ollama.LastRequest?.Messages.Last().Images);
    }

    [Fact]
    public async Task DetectedSpeechBargesIntoAnActiveModelTurn()
    {
        var calls = new MemoryCallRepository();
        var conversations = new MemoryConversationRepository();
        var ollama = new FakeOllamaClient(["A partial answer "]) { WaitAfterFirstChunk = true };
        var input = new FakeSpeechInput();
        var output = new FakeSpeechOutput();
        await using var coordinator = new CallCoordinator(
            calls, conversations, ollama, input, output, new FakeScreenShare());
        var session = await coordinator.StartAsync(
            new CallStartOptions(Model()), null, CancellationToken.None);
        var turn = coordinator.SubmitTextAsync("Start answering", CancellationToken.None);
        await ollama.FirstChunk.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await input.EmitAsync(new SpeechInputEvent(SpeechInputEventKind.SpeechStarted));
        await turn.WaitAsync(TimeSpan.FromSeconds(2));

        var assistant = conversations.Messages[session.ConversationId]
            .Single(item => item.Role == MessageRole.Assistant);
        Assert.Contains("\"interrupted\":true", assistant.MetadataJson);
        Assert.True(output.StopCount > 0);
        Assert.True(coordinator.State is CallState.Transcribing or CallState.Listening);
    }

    [Theory]
    [InlineData("First sentence. Second sentence!", 2)]
    [InlineData("Question? Tail", 1)]
    [InlineData("No punctuation yet", 0)]
    public void SentenceChunkerEmitsOnlyCompleteSpeechChunks(string value, int expectedCount)
    {
        var chunker = new SentenceChunker();
        var result = chunker.Append(value);

        Assert.Equal(expectedCount, result.Count);
        if (expectedCount == 0) Assert.Equal(value, chunker.Flush());
    }

    private static CallCoordinator CreateCoordinator(
        out MemoryCallRepository calls,
        out MemoryConversationRepository conversations,
        out FakeOllamaClient ollama,
        out FakeSpeechOutput speech,
        out FakeScreenShare screen)
    {
        calls = new();
        conversations = new();
        ollama = new(["Done."]);
        speech = new();
        screen = new();
        return new CallCoordinator(calls, conversations, ollama, new FakeSpeechInput(), speech, screen);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTimeOffset.UtcNow < timeout)
            await Task.Delay(10);
        Assert.True(condition(), "The asynchronous Call transition did not complete before the timeout.");
    }

    private static ModelDescriptor Model(bool vision = false) => new(
        "qwen-test",
        1,
        "qwen",
        "test",
        "test",
        vision
            ? new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Vision }
            : new HashSet<ToolCapability> { ToolCapability.Text },
        DateTimeOffset.UtcNow);

    private sealed class MemoryCallRepository : ICallRepository
    {
        public Dictionary<Guid, CallSession> Items { get; } = [];
        public Task UpsertAsync(CallSession session, CancellationToken cancellationToken)
        {
            Items[session.Id] = session;
            return Task.CompletedTask;
        }
        public Task<CallSession?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.GetValueOrDefault(id));
        public Task<IReadOnlyList<CallSession>> GetRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CallSession>>(Items.Values.OrderByDescending(item => item.StartedAt).Take(limit).ToArray());
    }

    private sealed class MemoryConversationRepository : IConversationRepository
    {
        public Dictionary<Guid, Conversation> Items { get; } = [];
        public Dictionary<Guid, List<ChatMessage>> Messages { get; } = [];

        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Conversation>>(Items.Values.Take(limit).ToArray());
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.GetValueOrDefault(id));
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.GetValueOrDefault(conversationId) ?? []);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken)
        {
            Items[conversation.Id] = conversation;
            Messages.TryAdd(conversation.Id, []);
            return Task.CompletedTask;
        }
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            Messages.TryAdd(message.ConversationId, []);
            Messages[message.ConversationId].Add(message);
            return Task.CompletedTask;
        }
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken)
        {
            Items.Remove(id);
            Messages.Remove(id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOllamaClient(IReadOnlyList<string> chunks) : IOllamaClient
    {
        public OllamaChatRequest? LastRequest { get; private set; }
        public bool WaitAfterFirstChunk { get; set; }
        public TaskCompletionSource FirstChunk { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([Model()]);

        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastRequest = request;
            for (var index = 0; index < chunks.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunks[index];
                if (index == 0)
                {
                    FirstChunk.TrySetResult();
                    if (WaitAfterFirstChunk)
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
            }
        }

        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(string.Concat(chunks));
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Concat(chunks), []));
    }

    private sealed class FakeSpeechInput : ISpeechInputService
    {
        private Func<SpeechInputEvent, CancellationToken, Task>? _callback;
        private CancellationToken _callbackToken;
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public IReadOnlyList<CallAudioDevice> Devices { get; } = [new("mic", "Test microphone", true)];
        public int StopCount { get; private set; }

        public Task StartAsync(
            SpeechInputOptions options,
            Func<SpeechInputEvent, CancellationToken, Task> onEvent,
            CancellationToken cancellationToken)
        {
            _callback = onEvent;
            _callbackToken = cancellationToken;
            return Task.CompletedTask;
        }
        public Task BeginPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EndPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }
        public Task EmitAsync(SpeechInputEvent value) =>
            _callback?.Invoke(value, _callbackToken) ?? Task.CompletedTask;
    }

    private sealed class FakeSpeechOutput : ISpeechOutputService
    {
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public IReadOnlyList<CallAudioDevice> Devices { get; } = [new("speaker", "Test speaker", true)];
        public IReadOnlyList<CallVoice> Voices { get; } = [new("voice", "Test voice", "en-GB", true)];
        public List<string> Spoken { get; } = [];
        public int StopCount { get; private set; }
        public Task SpeakAsync(string text, string? voiceName, string? outputDeviceId, CancellationToken cancellationToken)
        {
            Spoken.Add(text);
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScreenShare : IScreenShareService
    {
        public const string FrameData = "PRIVATE_FRAME_BASE64";
        public bool IsSupported => true;
        public bool IsSharing { get; private set; }
        public string? UnavailableReason => null;
        public ScreenShareSource? CurrentSource { get; private set; }
        public event EventHandler? SourceClosed;
        public event EventHandler<ScreenShareSnapshotEventArgs>? SnapshotAvailable;
        public int StopCount { get; private set; }
        public int GetSnapshotCount { get; private set; }
        public Task<ScreenShareSource> StartWithSystemPickerAsync(CancellationToken cancellationToken)
        {
            IsSharing = true;
            CurrentSource = new("screen-1", "Test screen", false);
            return Task.FromResult(CurrentSource);
        }
        public Task<ScreenShareSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken)
        {
            GetSnapshotCount++;
            return Task.FromResult<ScreenShareSnapshot?>(new(FrameData, 1280, 720, DateTimeOffset.UtcNow));
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            IsSharing = false;
            CurrentSource = null;
            return Task.CompletedTask;
        }

        public void RaiseSourceClosed() => SourceClosed?.Invoke(this, EventArgs.Empty);
        public void RaiseSnapshot(ScreenShareSnapshot value) => SnapshotAvailable?.Invoke(this, new(value));
    }
}
