using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class NotesReadAloudControllerTests
{
    [Fact]
    public async Task SelectedTextIsSpokenLocallyAndAuditedWithoutNetworkContent()
    {
        var speech = new FakeSpeechOutputService();
        var diagnostics = new RecordingDiagnostics();
        var controller = new NotesReadAloudController(
            speech,
            new FakeCallCoordinator(),
            diagnostics);
        try
        {
            await controller.ReadAsync("Selected paragraph.", "en-GB", CancellationToken.None);

            Assert.Equal("Selected paragraph.", speech.LastText);
            Assert.Equal("default-output", speech.LastOutputDeviceId);
            Assert.False(controller.IsActive);
            Assert.Contains(diagnostics.Events, value =>
                value.EventName == "read-aloud-completed"
                && value.Data.TryGetValue("networkContentSent", out var sent)
                && sent.Equals(bool.FalseString, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await controller.DisposeAsync();
        }
    }

    [Fact]
    public async Task StopInterruptsHeldSpeechWithoutWaitingForNaturalCompletion()
    {
        var speech = new FakeSpeechOutputService { HoldPlayback = true };
        var controller = new NotesReadAloudController(
            speech,
            new FakeCallCoordinator(),
            new RecordingDiagnostics());
        try
        {
            var read = controller.ReadAsync("A deliberately long passage.", "en-GB", CancellationToken.None);
            await speech.SpeakStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            await controller.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            await read.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(speech.StopCalls >= 1);
            Assert.True(speech.LastSpeakCancellation.IsCancellationRequested);
            Assert.False(controller.IsActive);
        }
        finally
        {
            await controller.DisposeAsync();
        }
    }

    [Fact]
    public async Task ActiveCallBlocksReadAloudBeforeSpeechStarts()
    {
        var speech = new FakeSpeechOutputService();
        var controller = new NotesReadAloudController(
            speech,
            new FakeCallCoordinator { Active = true },
            new RecordingDiagnostics());
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                controller.ReadAsync("Blocked text", "en-GB", CancellationToken.None));

            Assert.Contains("active Haven Call", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, speech.SpeakCalls);
        }
        finally
        {
            await controller.DisposeAsync();
        }
    }

    [Fact]
    public async Task RecoverySafeModeBlocksReadAloud()
    {
        var speech = new FakeSpeechOutputService();
        var controller = new NotesReadAloudController(
            speech,
            new FakeCallCoordinator(),
            new RecordingDiagnostics());
        RuntimeSafetyState.EnableSafeMode("test");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                controller.ReadAsync("Blocked text", "en-GB", CancellationToken.None));
            Assert.Equal(0, speech.SpeakCalls);
        }
        finally
        {
            RuntimeSafetyState.DisableSafeMode();
            await controller.DisposeAsync();
        }
    }

    private sealed class FakeSpeechOutputService : ISpeechOutputService
    {
        public bool HoldPlayback { get; init; }
        public int SpeakCalls { get; private set; }
        public int StopCalls { get; private set; }
        public string LastText { get; private set; } = string.Empty;
        public string? LastOutputDeviceId { get; private set; }
        public CancellationToken LastSpeakCancellation { get; private set; }
        public TaskCompletionSource SpeakStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public IReadOnlyList<CallVoice> Voices => [];
        public IReadOnlyList<CallAudioDevice> OutputDevices { get; } =
            [new("default-output", "Default speakers", true)];

        public async Task SpeakAsync(
            string text,
            CallVoice? voice,
            string? outputDeviceId,
            CancellationToken cancellationToken)
        {
            SpeakCalls++;
            LastText = text;
            LastOutputDeviceId = outputDeviceId;
            LastSpeakCancellation = cancellationToken;
            SpeakStarted.TrySetResult();
            if (HoldPlayback)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
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
        public CallState State => Active ? CallState.Listening : CallState.Idle;
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
