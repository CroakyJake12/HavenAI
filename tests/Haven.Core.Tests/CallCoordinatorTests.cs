/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/CallCoordinatorTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns CallCoordinatorTests, MemoryCallRepository, MemoryConversationRepository, FakeOllamaClient, FakeSpeechInput, FakeSpeechOutput, FakeScreenShare. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents call coordinator tests and keeps its related state and behavior together.
/// </summary>
public sealed class CallCoordinatorTests
{
    /// <summary>
    /// Performs the typed vision call streams speech and never persists frame data step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the coordinator rejects a second active call step owned by this component.
    /// </summary>
    [Fact]
    public async Task CoordinatorRejectsASecondActiveCall()
    {
        await using var coordinator = CreateCoordinator(out _, out _, out _, out _, out _);
        await coordinator.StartAsync(new CallStartOptions(Model()), null, CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAsync(new CallStartOptions(Model()), null, CancellationToken.None));

        Assert.Contains("already active", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the interrupt cancels generation and persists only marked partial transcript step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the speech source closure fails call and releases every media service step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the screen source closure fails call and releases every media service step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the non vision call does not read or send captured frames step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the detected speech barges into an active model turn step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the sentence chunker emits only complete speech chunks step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Creates coordinator with the invariants required by its callers.
    /// </summary>
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

    /// <summary>
    /// Performs wait until async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTimeOffset.UtcNow < timeout)
            await Task.Delay(10);
        Assert.True(condition(), "The asynchronous Call transition did not complete before the timeout.");
    }

    /// <summary>
    /// Performs the model step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Represents memory call repository and keeps its related state and behavior together.
    /// </summary>
    private sealed class MemoryCallRepository : ICallRepository
    {
        /// <summary>
        /// Gets or updates items, the bindable or domain state represented by this property.
        /// </summary>
        public Dictionary<Guid, CallSession> Items { get; } = [];
        /// <summary>
        /// Performs upsert async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task UpsertAsync(CallSession session, CancellationToken cancellationToken)
        {
            Items[session.Id] = session;
            return Task.CompletedTask;
        }
        /// <summary>
        /// Retrieves async for the current operation.
        /// </summary>
        public Task<CallSession?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.GetValueOrDefault(id));
        /// <summary>
        /// Retrieves recent async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<CallSession>> GetRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CallSession>>(Items.Values.OrderByDescending(item => item.StartedAt).Take(limit).ToArray());
    }

    /// <summary>
    /// Represents memory conversation repository and keeps its related state and behavior together.
    /// </summary>
    private sealed class MemoryConversationRepository : IConversationRepository
    {
        /// <summary>
        /// Gets or updates items, the bindable or domain state represented by this property.
        /// </summary>
        public Dictionary<Guid, Conversation> Items { get; } = [];
        /// <summary>
        /// Gets or updates messages, the bindable or domain state represented by this property.
        /// </summary>
        public Dictionary<Guid, List<ChatMessage>> Messages { get; } = [];

        /// <summary>
        /// Retrieves recent async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Conversation>>(Items.Values.Take(limit).ToArray());
        /// <summary>
        /// Retrieves async for the current operation.
        /// </summary>
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.GetValueOrDefault(id));
        /// <summary>
        /// Retrieves messages async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.GetValueOrDefault(conversationId) ?? []);
        /// <summary>
        /// Performs upsert conversation async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken)
        {
            Items[conversation.Id] = conversation;
            Messages.TryAdd(conversation.Id, []);
            return Task.CompletedTask;
        }
        /// <summary>
        /// Performs add message async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            Messages.TryAdd(message.ConversationId, []);
            Messages[message.ConversationId].Add(message);
            return Task.CompletedTask;
        }
        /// <summary>
        /// Performs delete conversation async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken)
        {
            Items.Remove(id);
            Messages.Remove(id);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Represents fake ollama client and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeOllamaClient(IReadOnlyList<string> chunks) : IOllamaClient
    {
        /// <summary>
        /// Gets or updates last request, the bindable or domain state represented by this property.
        /// </summary>
        public OllamaChatRequest? LastRequest { get; private set; }
        /// <summary>
        /// Gets or updates wait after first chunk, the bindable or domain state represented by this property.
        /// </summary>
        public bool WaitAfterFirstChunk { get; set; }
        /// <summary>
        /// Gets or updates first chunk, the bindable or domain state represented by this property.
        /// </summary>
        public TaskCompletionSource FirstChunk { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Reports whether is available async is true for the current state.
        /// </summary>
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        /// <summary>
        /// Retrieves models async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([Model()]);

        /// <summary>
        /// Performs stream chat async asynchronously so I/O does not block the caller's thread.
        /// </summary>
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

        /// <summary>
        /// Performs complete async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(string.Concat(chunks));
        /// <summary>
        /// Performs chat with tools async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Concat(chunks), []));
    }

    /// <summary>
    /// Represents fake speech input and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeSpeechInput : ISpeechInputService
    {
        /// <summary>
        /// Stores callback locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private Func<SpeechInputEvent, CancellationToken, Task>? _callback;
        /// <summary>
        /// Stores callback token locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private CancellationToken _callbackToken;
        /// <summary>
        /// Reports whether is available is true for the current state.
        /// </summary>
        public bool IsAvailable => true;
        /// <summary>
        /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
        /// </summary>
        public string? UnavailableReason => null;
        /// <summary>
        /// Gets or updates devices, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<CallAudioDevice> Devices { get; } = [new("mic", "Test microphone", true)];
        /// <summary>
        /// Gets or updates stop count, the bindable or domain state represented by this property.
        /// </summary>
        public int StopCount { get; private set; }

        /// <summary>
        /// Performs start async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task StartAsync(
            SpeechInputOptions options,
            Func<SpeechInputEvent, CancellationToken, Task> onEvent,
            CancellationToken cancellationToken)
        {
            _callback = onEvent;
            _callbackToken = cancellationToken;
            return Task.CompletedTask;
        }
        /// <summary>
        /// Performs begin push to talk async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task BeginPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs end push to talk async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task EndPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs stop async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }
        /// <summary>
        /// Performs emit async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task EmitAsync(SpeechInputEvent value) =>
            _callback?.Invoke(value, _callbackToken) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Represents fake speech output and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeSpeechOutput : ISpeechOutputService
    {
        /// <summary>
        /// Reports whether is available is true for the current state.
        /// </summary>
        public bool IsAvailable => true;
        /// <summary>
        /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
        /// </summary>
        public string? UnavailableReason => null;
        /// <summary>
        /// Gets or updates devices, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<CallAudioDevice> Devices { get; } = [new("speaker", "Test speaker", true)];
        /// <summary>
        /// Gets or updates voices, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<CallVoice> Voices { get; } = [new("voice", "Test voice", "en-GB", true)];
        /// <summary>
        /// Gets or updates spoken, the bindable or domain state represented by this property.
        /// </summary>
        public List<string> Spoken { get; } = [];
        /// <summary>
        /// Gets or updates stop count, the bindable or domain state represented by this property.
        /// </summary>
        public int StopCount { get; private set; }
        /// <summary>
        /// Performs speak async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task SpeakAsync(string text, string? voiceName, string? outputDeviceId, CancellationToken cancellationToken)
        {
            Spoken.Add(text);
            return Task.CompletedTask;
        }
        /// <summary>
        /// Performs stop async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Represents fake screen share and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeScreenShare : IScreenShareService
    {
        /// <summary>
        /// Stores frame data locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public const string FrameData = "PRIVATE_FRAME_BASE64";
        /// <summary>
        /// Reports whether is supported is true for the current state.
        /// </summary>
        public bool IsSupported => true;
        /// <summary>
        /// Reports whether is sharing is true for the current state.
        /// </summary>
        public bool IsSharing { get; private set; }
        /// <summary>
        /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
        /// </summary>
        public string? UnavailableReason => null;
        /// <summary>
        /// Gets or updates current source, the bindable or domain state represented by this property.
        /// </summary>
        public ScreenShareSource? CurrentSource { get; private set; }
        /// <summary>
        /// Stores source closed locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public event EventHandler? SourceClosed;
        /// <summary>
        /// Stores snapshot available locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public event EventHandler<ScreenShareSnapshotEventArgs>? SnapshotAvailable;
        /// <summary>
        /// Gets or updates stop count, the bindable or domain state represented by this property.
        /// </summary>
        public int StopCount { get; private set; }
        /// <summary>
        /// Retrieves snapshot count for the current operation.
        /// </summary>
        public int GetSnapshotCount { get; private set; }
        /// <summary>
        /// Performs start with system picker async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<ScreenShareSource> StartWithSystemPickerAsync(CancellationToken cancellationToken)
        {
            IsSharing = true;
            CurrentSource = new("screen-1", "Test screen", false);
            return Task.FromResult(CurrentSource);
        }
        /// <summary>
        /// Retrieves latest snapshot async for the current operation.
        /// </summary>
        public Task<ScreenShareSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken)
        {
            GetSnapshotCount++;
            return Task.FromResult<ScreenShareSnapshot?>(new(FrameData, 1280, 720, DateTimeOffset.UtcNow));
        }
        /// <summary>
        /// Performs stop async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            IsSharing = false;
            CurrentSource = null;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Performs the raise source closed step owned by this component.
        /// </summary>
        public void RaiseSourceClosed() => SourceClosed?.Invoke(this, EventArgs.Empty);
        /// <summary>
        /// Performs the raise snapshot step owned by this component.
        /// </summary>
        public void RaiseSnapshot(ScreenShareSnapshot value) => SnapshotAvailable?.Invoke(this, new(value));
    }
}
