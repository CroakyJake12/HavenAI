using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell.Overlays;
using Haven.UI;
using Container = Haven.UI.Components.Container;
using HavenText = Haven.UI.Components.Text;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class GlobalCallHavenSceneTests
{
    [Fact]
    public void Scene_exposes_existing_voice_modes_and_runtime_choices()
    {
        var coordinator = new FakeCallCoordinator();
        using var viewModel = new InChatCallWidgetViewModel(coordinator, new StubConversationRepository());
        viewModel.AttachConversation(Guid.NewGuid(), TestModel());
        using var scene = new GlobalCallHavenScene(viewModel);

        scene.Root.ValidateUniqueNames();
        Assert.True(scene.CallButton.GetValue(HavenProperties.Enabled));
        Assert.Contains("Hands-free", scene.VoiceMode.Items);
        Assert.Contains("Push to talk", scene.VoiceMode.Items);
        var pushToTalkIndex = Array.IndexOf(scene.VoiceMode.Items.ToArray(), "Push to talk");
        scene.VoiceMode.SelectedIndex = pushToTalkIndex;
        Assert.Equal(CallInputMode.PushToTalk, viewModel.InputMode);

        Assert.Contains("General Voice", scene.VoiceProfile.Items);
        var lessonIndex = Array.IndexOf(scene.VoiceProfile.Items.ToArray(), "Lesson Voice");
        Assert.True(lessonIndex >= 0);
        scene.VoiceProfile.SelectedIndex = lessonIndex;
        Assert.Equal("lesson", viewModel.SelectedVoiceProfile?.Id);

        scene.Reasoning.Value = 75;
        Assert.Equal(EffortLevel.High, viewModel.Effort);
        Assert.Equal(75, viewModel.ReasoningPercent);

        scene.Microphone.SelectedIndex = 1;
        Assert.Equal("mic-two", viewModel.SelectedInputDevice?.Id);
        scene.VoiceChoice.SelectedIndex = 0;
        Assert.Equal("voice-one", viewModel.SelectedVoice?.Id);
    }

    [Fact]
    public void Scene_keeps_typed_and_file_context_in_the_existing_voice_session_adapter()
    {
        var coordinator = new FakeCallCoordinator();
        using var viewModel = new InChatCallWidgetViewModel(coordinator, new StubConversationRepository());
        using var scene = new GlobalCallHavenScene(viewModel);

        scene.TranscriptInput.Text = "  explain this step  ";
        Assert.Equal("  explain this step  ", viewModel.TypedTranscript);

        scene.AddContextFiles(["lesson.pdf", "diagram.png", "lesson.pdf"]);
        Assert.Equal("lesson.pdf  •  diagram.png", scene.ContextItems.Content);
        Assert.False(scene.TranscriptInput.GetValue(HavenProperties.Enabled));
        Assert.False(scene.SendButton.GetValue(HavenProperties.Enabled));
    }

    [AvaloniaFact]
    public async Task Scene_shows_retryable_microphone_degradation_without_ending_voice()
    {
        var coordinator = new FakeCallCoordinator { Active = true };
        using var viewModel = new InChatCallWidgetViewModel(coordinator, new StubConversationRepository());
        using var scene = new GlobalCallHavenScene(viewModel);
        const string message = "Microphone permission is required for Haven Voice. Typed transcript mode is ready.";

        coordinator.RaiseInputStatus(new VoiceInputStatus(
            VoiceInputState.PermissionDenied,
            message,
            CanRetry: true));
        await FlushUiAsync();

        Assert.True(viewModel.IsActive);
        Assert.True(viewModel.IsVoiceInputDegraded);
        Assert.True(viewModel.CanRetryVoiceInput);
        Assert.Equal("Retry mic", scene.MuteButton.Content);
        Assert.Equal(message, scene.InputStatusText.Content);
        Assert.True(scene.MuteButton.GetValue(HavenProperties.Enabled));
    }

    [AvaloniaFact]
    public async Task Scene_updates_live_voice_hot_paths_without_rebuilding_transcript_bubble()
    {
        var coordinator = new FakeCallCoordinator();
        using var viewModel = new InChatCallWidgetViewModel(coordinator, new StubConversationRepository());
        using var scene = new GlobalCallHavenScene(viewModel);
        var messageId = Guid.NewGuid();

        coordinator.RaiseTranscript(new CallTranscriptEventArgs(
            messageId, MessageRole.User, "Hello", isDelta: false, isFinal: false));
        await FlushUiAsync();

        var bubble = Assert.IsType<Container>(Assert.Single(scene.TranscriptTurns.Children));
        var body = bubble.Children.OfType<HavenText>().Last();
        Assert.Equal("Hello", body.Content);

        coordinator.RaiseTranscript(new CallTranscriptEventArgs(
            messageId, MessageRole.User, " world", isDelta: true, isFinal: false));
        await FlushUiAsync();

        var updatedBubble = Assert.IsType<Container>(Assert.Single(scene.TranscriptTurns.Children));
        var updatedBody = updatedBubble.Children.OfType<HavenText>().Last();
        Assert.Same(bubble, updatedBubble);
        Assert.Same(body, updatedBody);
        Assert.Equal("Hello world", updatedBody.Content);

        coordinator.RaiseAudio(0.8);
        await FlushUiAsync();

        Assert.Same(bubble, Assert.Single(scene.TranscriptTurns.Children));
        Assert.Equal(0.8, scene.AudioLevel.Value, 3);

        coordinator.RaiseState(CallState.Thinking, "Thinking");
        await FlushUiAsync();

        Assert.Same(bubble, Assert.Single(scene.TranscriptTurns.Children));
        Assert.Equal("Thinking", scene.StatusText.Content);
    }

    [Fact]
    public void Scene_drag_header_consumes_pointer_sequence_and_reports_incremental_delta()
    {
        var coordinator = new FakeCallCoordinator();
        using var viewModel = new InChatCallWidgetViewModel(coordinator, new StubConversationRepository());
        HavenPoint? observed = null;
        using var scene = new GlobalCallHavenScene(viewModel, delta => observed = delta);
        var handle = Assert.Single(scene.Root.DescendantsAndSelf().OfType<VoiceDragHandle>());
        var target = Assert.IsAssignableFrom<IHavenPointerInputTarget>(handle);

        Assert.All(handle.Children, child =>
            Assert.Equal(HavenPointerEvents.None, child.GetValue(HavenProperties.PointerEvents)));
        Assert.True(target.PointerPressed(new HavenPointerInput(
            new HavenPoint(10, 10), new HavenPoint(4, 4), HavenPointerKind.Mouse)));
        Assert.True(target.PointerMoved(new HavenPointerInput(
            new HavenPoint(16, 7), new HavenPoint(10, 1), HavenPointerKind.Mouse)));
        Assert.Equal(new HavenPoint(6, -3), observed);

        observed = null;
        Assert.True(target.PointerMoved(new HavenPointerInput(
            new HavenPoint(18, 11), new HavenPoint(12, 5), HavenPointerKind.Mouse)));
        Assert.Equal(new HavenPoint(2, 4), observed);
        Assert.True(target.PointerReleased(new HavenPointerInput(
            new HavenPoint(18, 11), new HavenPoint(12, 5), HavenPointerKind.Mouse)));
        Assert.False(target.PointerMoved(new HavenPointerInput(
            new HavenPoint(20, 12), new HavenPoint(14, 6), HavenPointerKind.Mouse)));
    }

    private static async Task FlushUiAsync() =>
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });

    private static ModelDescriptor TestModel() => new(
        "voice-test",
        1_000_000,
        "test",
        "1B",
        "Q4",
        new HashSet<ToolCapability>(),
        DateTimeOffset.UtcNow);

    private sealed class StubConversationRepository : IConversationRepository
    {
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCallCoordinator : ICallCoordinator, IVoiceInputStatusSource
    {
        private VoiceInputStatus _inputStatus = new(VoiceInputState.Ready, "Microphone ready.");
        public bool Active { get; set; }
        public CallState State => CallState.Idle;
        public CallSession? CurrentSession => null;
        public Conversation? CurrentConversation => null;
        public CallCapabilities Capabilities { get; } = new(
            true,
            true,
            true,
            null,
            null,
            null,
            [new CallAudioDevice("mic-one", "Mic One", true), new CallAudioDevice("mic-two", "Mic Two")],
            [new CallAudioDevice("speaker-one", "Speaker One", true)],
            [new CallVoice("voice-one", "Voice One", null, true)]);
        public bool IsActive => Active;
        public bool IsMuted => false;
        public bool IsScreenSharing => false;
        public VoiceInputStatus InputStatus => _inputStatus;

        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public event EventHandler<CallTranscriptEventArgs>? TranscriptChanged;
        public event EventHandler<CallAudioLevelEventArgs>? AudioLevelChanged;
        public event EventHandler<ScreenShareSnapshotEventArgs>? ScreenPreviewChanged;
        public event EventHandler<VoiceInputStatusChangedEventArgs>? InputStatusChanged;

        public Task<CallSession> StartAsync(CallStartOptions options, SpeechModelInfo? speechModel, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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

        public void RaiseState(CallState state, string status) => StateChanged?.Invoke(this, new CallStateChangedEventArgs(state, status));
        public void RaiseInputStatus(VoiceInputStatus status)
        {
            _inputStatus = status;
            InputStatusChanged?.Invoke(this, new VoiceInputStatusChangedEventArgs(status));
        }
        public void RaiseTranscript(CallTranscriptEventArgs args) => TranscriptChanged?.Invoke(this, args);
        public void RaiseAudio(double level) => AudioLevelChanged?.Invoke(this, new CallAudioLevelEventArgs(level));
        public void RaiseScreen(ScreenShareSnapshot snapshot) => ScreenPreviewChanged?.Invoke(this, new ScreenShareSnapshotEventArgs(snapshot));
    }
}
