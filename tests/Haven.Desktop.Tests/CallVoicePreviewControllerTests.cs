using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class CallVoicePreviewControllerTests
{
    [Fact]
    public async Task ActiveCallBlocksPreviewWithoutStoppingSharedSpeech()
    {
        var speech = new FakeSpeechOutputService();
        var controller = new CallVoicePreviewController(
            speech,
            new RecordingDiagnostics(),
            new FakeCallCoordinator { Active = true });
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                controller.PreviewAsync(
                    new CallVoice("voice", "Voice", "en-GB", true),
                    "default",
                    CancellationToken.None));

            Assert.Contains("active Haven Call", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, speech.SpeakCalls);
            Assert.Equal(0, speech.StopCalls);
        }
        finally
        {
            await controller.DisposeAsync();
        }
    }

    [Fact]
    public async Task InactiveStopAndDisposeDoNotInterruptSharedSpeech()
    {
        var speech = new FakeSpeechOutputService();
        var controller = new CallVoicePreviewController(
            speech,
            new RecordingDiagnostics(),
            new FakeCallCoordinator());

        await controller.StopAsync(CancellationToken.None);
        await controller.DisposeAsync();

        Assert.Equal(0, speech.StopCalls);
    }

    private sealed class FakeSpeechOutputService : ISpeechOutputService
    {
        public int SpeakCalls { get; private set; }
        public int StopCalls { get; private set; }
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public IReadOnlyList<CallVoice> Voices { get; } = [new("voice", "Voice", "en-GB", true)];
        public IReadOnlyList<CallAudioDevice> Devices { get; } = [new("default", "Default", true)];

        public Task SpeakAsync(
            string text,
            string? voiceName,
            string? outputDeviceId,
            CancellationToken cancellationToken)
        {
            SpeakCalls++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCallCoordinator : ICallCoordinator
    {
        public bool Active { get; init; }
        public CallState State => Active ? CallState.Speaking : CallState.Idle;
        public CallSession? CurrentSession => null;
        public Conversation? CurrentConversation => null;
        public CallCapabilities Capabilities { get; } = new(false, false, false, null, null, null, [], [], []);
        public bool IsActive => Active;
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

    private sealed class RecordingDiagnostics : IProductionDiagnostics
    {
        public ValueTask WriteAsync(
            ReliabilitySeverity severity,
            string component,
            string eventName,
            string message,
            IReadOnlyDictionary<string, string>? data = null,
            string? correlationId = null,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(
            int limit,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReliabilityEvent>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
