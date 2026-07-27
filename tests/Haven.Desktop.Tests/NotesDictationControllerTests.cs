/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/NotesDictationControllerTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns NotesDictationControllerTests, FakeSpeechInputService, FakeSpeechModelManager, FakeCallCoordinator, RecordingDiagnostics, RecordedEvent. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents notes dictation controller tests and keeps its related state and behavior together.
/// </summary>
public sealed class NotesDictationControllerTests
{
    /// <summary>
    /// Performs the final local transcript applies once and stops capture step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the active call blocks notes microphone before capture starts step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the missing installed whisper model fails before capture step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the recovery safe mode blocks notes dictation step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the rejected inactive and disposed controller does not stop shared call microphone step owned by this component.
    /// </summary>
    [Fact]
    public async Task RejectedInactiveAndDisposedControllerDoesNotStopSharedCallMicrophone()
    {
        var speech = new FakeSpeechInputService();
        var controller = new NotesDictationController(
            speech,
            new FakeSpeechModelManager(installed: true),
            new FakeCallCoordinator { Active = true },
            new RecordingDiagnostics());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.StartOneUtteranceAsync((_, _) => Task.CompletedTask, CancellationToken.None));
        await controller.StopAsync(CancellationToken.None);
        await controller.DisposeAsync();

        Assert.Equal(0, speech.StartCalls);
        Assert.Equal(0, speech.StopCalls);
    }

    /// <summary>
    /// Performs wait until asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(5);
        Assert.True(condition());
    }

    /// <summary>
    /// Represents fake speech input service and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeSpeechInputService : ISpeechInputService
    {
        /// <summary>
        /// Gets or updates start calls, the bindable or domain state represented by this property.
        /// </summary>
        public int StartCalls { get; private set; }
        /// <summary>
        /// Gets or updates stop calls, the bindable or domain state represented by this property.
        /// </summary>
        public int StopCalls { get; private set; }
        /// <summary>
        /// Reports whether available applies to the current state.
        /// </summary>
        public bool IsAvailable => true;
        /// <summary>
        /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
        /// </summary>
        public string? UnavailableReason => null;
        /// <summary>
        /// Gets or updates devices, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<CallAudioDevice> Devices { get; } = [new("default", "Default microphone", true)];

        /// <summary>
        /// Performs start asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async Task StartAsync(
            SpeechInputOptions options,
            Func<SpeechInputEvent, CancellationToken, Task> onEvent,
            CancellationToken cancellationToken)
        {
            StartCalls++;
            await onEvent(new SpeechInputEvent(SpeechInputEventKind.SpeechStarted), cancellationToken);
            await onEvent(new SpeechInputEvent(SpeechInputEventKind.FinalTranscript, "spoken passage"), cancellationToken);
        }

        /// <summary>
        /// Performs begin push to talk asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task BeginPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs end push to talk asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task EndPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs stop asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Represents fake speech model manager and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeSpeechModelManager(bool installed) : ISpeechModelManager
    {
        /// <summary>
        /// Retrieves models async for the current operation.
        /// </summary>
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

        /// <summary>
        /// Performs download asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<SpeechModelInfo> DownloadAsync(
            SpeechModelSize size,
            IProgress<double>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        /// <summary>
        /// Performs delete asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteAsync(SpeechModelSize size, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Represents fake call coordinator and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeCallCoordinator : ICallCoordinator
    {
        /// <summary>
        /// Gets or updates active, the bindable or domain state represented by this property.
        /// </summary>
        public bool Active { get; init; }
        /// <summary>
        /// Gets or updates state, the bindable or domain state represented by this property.
        /// </summary>
        public CallState State => Active ? CallState.Listening : CallState.Idle;
        /// <summary>
        /// Gets or updates current session, the bindable or domain state represented by this property.
        /// </summary>
        public CallSession? CurrentSession => null;
        /// <summary>
        /// Gets or updates current conversation, the bindable or domain state represented by this property.
        /// </summary>
        public Conversation? CurrentConversation => null;
        /// <summary>
        /// Gets or updates capabilities, the bindable or domain state represented by this property.
        /// </summary>
        public CallCapabilities Capabilities { get; } = new(false, false, false, null, null, null, [], [], []);
        /// <summary>
        /// Reports whether active applies to the current state.
        /// </summary>
        public bool IsActive => Active;
        /// <summary>
        /// Reports whether muted applies to the current state.
        /// </summary>
        public bool IsMuted => false;
        /// <summary>
        /// Reports whether screen sharing applies to the current state.
        /// </summary>
        public bool IsScreenSharing => false;
        /// <summary>
        /// Stores state changed locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        /// <summary>
        /// Stores transcript changed locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public event EventHandler<CallTranscriptEventArgs>? TranscriptChanged;
        /// <summary>
        /// Stores audio level changed locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public event EventHandler<CallAudioLevelEventArgs>? AudioLevelChanged;
        /// <summary>
        /// Stores screen preview changed locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public event EventHandler<ScreenShareSnapshotEventArgs>? ScreenPreviewChanged;
        /// <summary>
        /// Performs start asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<CallSession> StartAsync(CallStartOptions options, SpeechModelInfo? speechModel, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <summary>
        /// Performs submit text asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task SubmitTextAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs begin push to talk asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task BeginPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs end push to talk asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task EndPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs set muted asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task SetMutedAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs pause asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs resume asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs start screen share asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task StartScreenShareAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs stop screen share asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task StopScreenShareAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs interrupt asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task InterruptAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs end asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task EndAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs dispose asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>
        /// Performs the suppress unused event warnings step owned by this component.
        /// </summary>
        public void SuppressUnusedEventWarnings()
        {
            _ = StateChanged;
            _ = TranscriptChanged;
            _ = AudioLevelChanged;
            _ = ScreenPreviewChanged;
        }
    }

    /// <summary>
    /// Represents recording diagnostics and keeps its related state and behavior together.
    /// </summary>
    private sealed class RecordingDiagnostics : IProductionDiagnostics
    {
        /// <summary>
        /// Gets or updates events, the bindable or domain state represented by this property.
        /// </summary>
        public List<RecordedEvent> Events { get; } = [];

        /// <summary>
        /// Performs write asynchronously so I/O does not block the caller's thread.
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
            Events.Add(new RecordedEvent(eventName, data ?? new Dictionary<string, string>()));
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Performs read recent asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReliabilityEvent>>([]);

        /// <summary>
        /// Performs dispose asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Represents recorded event and keeps its related state and behavior together.
    /// </summary>
    private sealed record RecordedEvent(string EventName, IReadOnlyDictionary<string, string> Data);
}
