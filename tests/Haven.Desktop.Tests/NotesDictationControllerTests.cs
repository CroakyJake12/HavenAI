using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class NotesDictationControllerTests
{
    [Fact]
    public async Task FinalLocalTranscriptAppliesOnceAndStopsCapture()
    {
        var speech = new FakeSpeechInputService();
        var diagnostics = new RecordingDiagnostics();
        var controller = new NotesDictationController(
            speech,
            new FakeSpeechModelManager(installed: true),
            new FakeCallCoordinator(),
            diagnostics);
        var applied = new List<string>();
        try
        {
            await controller.StartOneUtteranceAsync((text, _) =>
            {
                applied.Add(text);
                return Task.CompletedTask;
            }, CancellationToken.None);
            await WaitUntilAsync(() => speech.StopCalls > 0);

            Assert.Equal(["spoken passage"], applied);
            Assert.Equal(1, speech.StartCalls);
            Assert.True(speech.StopCalls >= 1);
            Assert.False(controller.IsActive);
            Assert.Contains(diagnostics.Events, item =>
                item.EventName == "dictation-applied"
                && item.Data.TryGetValue("rawAudioPersisted", out var value)
                && value.Equals(bool.FalseString, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await controller.DisposeAsync();
        }
    }

    [Fact]
    public async Task ActiveCallBlocksNotesMicrophoneBeforeCaptureStarts()
    {
        var speech = new FakeSpeechInputService();
        var controller = new NotesDictationController(
            speech,
            new FakeSpeechModelManager(installed: true),
            new FakeCallCoordinator { Active = true },
            new RecordingDiagnostics());
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                controller.StartOneUtteranceAsync((_, _) => Task.CompletedTask, CancellationToken.None));

            Assert.Contains("active Haven Call", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, speech.StartCalls);
        }
        finally
        {
            await controller.DisposeAsync();
        }
    }

    [Fact]
    public async Task MissingInstalledWhisperModelFailsBeforeCapture()
    {
        var speech = new FakeSpeechInputService();
        var controller = new NotesDictationController(
            speech,
            new FakeSpeechModelManager(installed: false),
            new FakeCallCoordinator(),
            new RecordingDiagnostics());
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                controller.StartOneUtteranceAsync((_, _) => Task.CompletedTask, CancellationToken.None));

            Assert.Contains("Download a local Whisper", error.Message, StringComparison.Ordinal);
            Assert.Equal(0, speech.StartCalls);
        }
        finally
        {
            await controller.DisposeAsync();
        }
    }

    [Fact]
    public async Task RecoverySafeModeBlocksNotesDictation()
    {
        var speech = new FakeSpeechInputService();
        var controller = new NotesDictationController(
            speech,
            new FakeSpeechModelManager(installed: true),
            new FakeCallCoordinator(),
            new RecordingDiagnostics());
        RuntimeSafetyState.EnableSafeMode("test");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                controller.StartOneUtteranceAsync((_, _) => Task.CompletedTask, CancellationToken.None));
            Assert.Equal(0, speech.StartCalls);
        }
        finally
        {
            RuntimeSafetyState.DisableSafeMode();
            await controller.DisposeAsync();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(5);
        Assert.True(condition());
    }

    private sealed class FakeSpeechInputService : ISpeechInputService
    {
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public IReadOnlyList<CallAudioDevice> Devices { get; } = [new("default", "Default microphone", true)];

        public async Task StartAsync(
            SpeechInputOptions options,
            Func<SpeechInputEvent, CancellationToken, Task> onEvent,
            CancellationToken cancellationToken)
        {
            StartCalls++;
            await onEvent(new SpeechInputEvent(SpeechInputEventKind.SpeechStarted), cancellationToken);
            await onEvent(new SpeechInputEvent(SpeechInputEventKind.FinalTranscript, "spoken passage"), cancellationToken);
        }

        public Task BeginPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EndPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSpeechModelManager(bool installed) : ISpeechModelManager
    {
        public Task<IReadOnlyList<SpeechModelInfo>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SpeechModelInfo>>
            ([
                new SpeechModelInfo(
                    SpeechModelSize.Base,
                    "Base · recommended",
                    "ggml-base.bin",
                    142_000_000,
                    installed,
                    installed ? "C:\\models\\ggml-base.bin" : "")
            ]);

        public Task<SpeechModelInfo> DownloadAsync(
            SpeechModelSize size,
            IProgress<double>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(SpeechModelSize size, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCallCoordinator : ICallCoordinator
    {
        public bool Active { get; init; }
        public CallState State => Active ? CallState.Listening : CallState.Idle;
        public CallSession? CurrentSession => null;
        public Conversation? CurrentConversation => null;
        public CallCapabilities Capabilities { get; } = new(false, false, false, null, null, null, [], [], []);
        public bool IsActive => Active;
        public bool IsMuted => false;
        public bool IsScreenSharing => false;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public event EventHandler<CallTranscriptEventArgs>? TranscriptChanged;
        public event EventHandler<CallAudioLevelEventArgs>? AudioLevelChanged;
        public event EventHandler<ScreenShareSnapshotEventArgs>? ScreenPreviewChanged;
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

        public void SuppressUnusedEventWarnings()
        {
            _ = StateChanged;
            _ = TranscriptChanged;
            _ = AudioLevelChanged;
            _ = ScreenPreviewChanged;
        }
    }

    private sealed class RecordingDiagnostics : IProductionDiagnostics
    {
        public List<RecordedEvent> Events { get; } = [];

        public ValueTask WriteAsync(
            ReliabilitySeverity severity,
            string component,
            string eventName,
            string message,
            IReadOnlyDictionary<string, string>? data = null,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new RecordedEvent(eventName, data ?? new Dictionary<string, string>()));
            return ValueTask.CompletedTask;
        }

        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReliabilityEvent>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record RecordedEvent(string EventName, IReadOnlyDictionary<string, string> Data);
}
