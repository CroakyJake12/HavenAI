using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell.Overlays;
using Haven.UI.Components;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class VoiceSessionContinuityTests
{
    [Fact]
    public void Recreated_voice_presentation_hydrates_the_existing_runtime_session()
    {
        var now = new DateTimeOffset(2026, 8, 17, 19, 0, 0, TimeSpan.Zero);
        var conversation = new Conversation(
            Guid.NewGuid(),
            HavenMode.Chat,
            ConversationKind.Call,
            "Voice call",
            null,
            null,
            false,
            false,
            now,
            now);
        var session = new CallSession(
            Guid.NewGuid(),
            conversation.Id,
            "voice-model",
            "mic-one",
            "speaker-one",
            "Voice One",
            CallInputMode.HandsFree,
            true,
            CallSessionStatus.Active,
            now);
        var coordinator = new ActiveCallCoordinator(session, conversation);

        using var viewModel = new InChatCallWidgetViewModel(coordinator, new StubConversationRepository());
        using var scene = new GlobalCallHavenScene(viewModel);

        Assert.True(viewModel.IsActive);
        Assert.True(viewModel.IsOpen);
        Assert.True(viewModel.IsMuted);
        Assert.Equal(conversation.Id, viewModel.LinkedCallConversationId);
        Assert.Equal("Speaking", viewModel.Status);
        Assert.Equal("End", scene.CallButton.Content);
        Assert.Equal(ButtonVariant.Danger, scene.CallButton.Variant);
        Assert.Equal("Mic off", scene.MuteButton.Content);
        Assert.Equal(0, coordinator.StartCount);
    }

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

    private sealed class ActiveCallCoordinator(CallSession session, Conversation conversation) : ICallCoordinator
    {
        public int StartCount { get; private set; }
        public CallState State => CallState.Speaking;
        public CallSession? CurrentSession => session;
        public Conversation? CurrentConversation => conversation;
        public CallCapabilities Capabilities { get; } = new(
            true,
            true,
            true,
            null,
            null,
            null,
            [new CallAudioDevice("mic-one", "Mic One", true)],
            [new CallAudioDevice("speaker-one", "Speaker One", true)],
            [new CallVoice("voice-one", "Voice One", null, true)]);
        public bool IsActive => true;
        public bool IsMuted => true;
        public bool IsScreenSharing => true;

        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<CallTranscriptEventArgs>? TranscriptChanged { add { } remove { } }
        public event EventHandler<CallAudioLevelEventArgs>? AudioLevelChanged { add { } remove { } }
        public event EventHandler<ScreenShareSnapshotEventArgs>? ScreenPreviewChanged { add { } remove { } }

        public Task<CallSession> StartAsync(CallStartOptions options, SpeechModelInfo? speechModel, CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.FromResult(session);
        }

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
}
