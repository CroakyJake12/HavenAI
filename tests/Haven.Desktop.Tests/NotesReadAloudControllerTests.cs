/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/NotesReadAloudControllerTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns NotesReadAloudControllerTests, FakeSpeechOutputService, FakeCallCoordinator, RecordingDiagnostics, RecordedEvent. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents notes read aloud controller tests and keeps its related state and behavior together.
/// </summary>
public sealed class NotesReadAloudControllerTests
{
    /// <summary>
    /// Performs the selected text is spoken locally and audited without network content step owned by this component.
    /// </summary>
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
            Assert.Null(speech.LastVoiceName);
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

    /// <summary>
    /// Performs the language matched voice identifier is passed to speech service step owned by this component.
    /// </summary>
    [Fact]
    public async Task LanguageMatchedVoiceIdentifierIsPassedToSpeechService()
    {
        var speech = new FakeSpeechOutputService
        {
            AvailableVoices =
            [
                new CallVoice("voice-en-us", "English US", "en-US", true),
                new CallVoice("voice-en-gb", "English UK", "en-GB", false)
            ]
        };
        var controller = new NotesReadAloudController(
            speech,
            new FakeCallCoordinator(),
            new RecordingDiagnostics());
        try
        {
            await controller.ReadAsync("Selected paragraph.", "en-GB", CancellationToken.None);

            Assert.Equal("voice-en-gb", speech.LastVoiceName);
        }
        finally
        {
            await controller.DisposeAsync();
        }
    }

    /// <summary>
    /// Performs the stop interrupts held speech without waiting for natural completion step owned by this component.
    /// </summary>
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
            await speech.SpeakStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            await controller.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await read.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Assert.True(speech.StopCalls >= 1);
            Assert.True(speech.LastSpeakCancellation.IsCancellationRequested);
            Assert.False(controller.IsActive);
        }
        finally
        {
            await controller.DisposeAsync();
        }
    }

    /// <summary>
    /// Performs the active call blocks read aloud before speech starts step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the recovery safe mode blocks read aloud step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the inactive stop and dispose do not interrupt shared call speech step owned by this component.
    /// </summary>
    [Fact]
    public async Task InactiveStopAndDisposeDoNotInterruptSharedCallSpeech()
    {
        var speech = new FakeSpeechOutputService();
        var controller = new NotesReadAloudController(
            speech,
            new FakeCallCoordinator { Active = true },
            new RecordingDiagnostics());

        await controller.StopAsync(CancellationToken.None);
        await controller.DisposeAsync();

        Assert.Equal(0, speech.StopCalls);
    }

    /// <summary>
    /// Verifies that chunk splitting prefers sentence boundaries and never exceeds the limit.
    /// </summary>
    [Fact]
    public void SplitIntoChunksPrefersSentenceBoundariesWithinMaximumLength()
    {
        var sentence = "Haven reads long documents locally without sending any text to a network service.";
        var text = string.Join(' ', Enumerable.Repeat(sentence, 20));

        var chunks = NotesReadAloudController.SplitIntoChunks(text);

        Assert.True(chunks.Count > 1, "expected a long document to produce several chunks");
        Assert.All(chunks, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk)));
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 600, "every chunk must respect the maximum length"));
        Assert.All(chunks, chunk => Assert.EndsWith(".", chunk, StringComparison.Ordinal));
        Assert.Equal(text, string.Join(' ', chunks));
    }

    /// <summary>
    /// Verifies that an oversize sentence without terminators is hard-split at word boundaries.
    /// </summary>
    [Fact]
    public void SplitIntoChunksHardSplitsOversizeSentencesAtWordBoundaries()
    {
        var longSentence = string.Concat(Enumerable.Repeat("word ", 200)).Trim();

        var chunks = NotesReadAloudController.SplitIntoChunks(longSentence, 120);

        Assert.True(chunks.Count >= 8, "expected a 999-character sentence to be hard-split");
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 120, "every hard-split chunk must respect the limit"));
        Assert.Equal(longSentence, string.Join(' ', chunks));
    }

    /// <summary>
    /// Verifies that blank input produces no chunks and is rejected before speaking.
    /// </summary>
    [Fact]
    public async Task BlankLongFormTextProducesNoChunksAndIsRejectedBeforeSpeaking()
    {
        var speech = new FakeSpeechOutputService();
        var controller = new NotesReadAloudController(
            speech,
            new FakeCallCoordinator(),
            new RecordingDiagnostics());
        try
        {
            Assert.Empty(NotesReadAloudController.SplitIntoChunks(" \n\t "));

            var error = await Assert.ThrowsAsync<ArgumentException>(() =>
                controller.SpeakLongFormAsync("   ", null, CancellationToken.None));

            Assert.Contains("no readable text", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, speech.SpeakCalls);
        }
        finally
        {
            await controller.DisposeAsync();
        }
    }

    /// <summary>
    /// Verifies continuous reading speaks every chunk in order and reports completion honestly.
    /// </summary>
    [Fact]
    public async Task LongFormSpeaksEveryChunkInOrderAndReportsCompletion()
    {
        var speech = new FakeSpeechOutputService();
        var diagnostics = new RecordingDiagnostics();
        var controller = new NotesReadAloudController(
            speech,
            new FakeCallCoordinator(),
            diagnostics);
        var progress = new List<double>();
        controller.ProgressChanged += progress.Add;
        try
        {
            var chunks = new[] { "First passage.", "Second passage.", "Third passage." };
            await controller.SpeakLongFormAsync(chunks, "en-GB", CancellationToken.None);

            Assert.Equal(chunks, speech.SpokenTexts);
            Assert.Equal(3, speech.SpeakCalls);
            Assert.Equal("default-output", speech.LastOutputDeviceId);
            Assert.False(controller.IsActive);
            Assert.False(controller.IsReading);
            Assert.False(controller.IsPaused);
            Assert.Equal(0, controller.ChunkCount);
            Assert.True(progress.Count > 0);
            Assert.Equal(1d, progress[^1]);
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

    /// <summary>
    /// Verifies skip forward restarts speech at the next chunk.
    /// </summary>
    [Fact]
    public async Task SkipForwardRestartsSpeechAtTheNextChunk()
    {
        var speech = new FakeSpeechOutputService { HoldPlayback = true };
        var controller = new NotesReadAloudController(
            speech,
            new FakeCallCoordinator(),
            new RecordingDiagnostics());
        try
        {
            var read = controller.SpeakLongFormAsync(
                ["Alpha section.", "Beta section.", "Gamma section."],
                null,
                CancellationToken.None);
            await WaitForAsync(() => speech.SpokenTexts.Count >= 1, "the first chunk");
            var firstChunkCancellation = speech.LastSpeakCancellation;

            await controller.SkipForwardAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await WaitForAsync(() => speech.SpokenTexts.Count >= 2, "the second chunk");

            Assert.Equal("Beta section.", speech.SpokenTexts[1]);
            Assert.Equal(1, controller.CurrentChunkIndex);
            Assert.True(firstChunkCancellation.IsCancellationRequested);

            await controller.StopAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await read.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.False(controller.IsReading);
        }
        finally
        {
            await controller.DisposeAsync();
        }
    }

    /// <summary>
    /// Verifies pause retains the queue position and resume re-speaks the retained current chunk.
    /// </summary>
    [Fact]
    public async Task PauseRetainsPositionAndResumeRespeaksCurrentChunk()
    {
        var speech = new FakeSpeechOutputService { HoldPlayback = true };
        var controller = new NotesReadAloudController(
            speech,
            new FakeCallCoordinator(),
            new RecordingDiagnostics());
        try
        {
            var read = controller.SpeakLongFormAsync(
                ["Opening section.", "Middle section.", "Closing section."],
                null,
                CancellationToken.None);
            await WaitForAsync(() => speech.SpokenTexts.Count >= 1, "the first chunk");

            await controller.PauseAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Assert.True(controller.IsActive);
            Assert.True(controller.IsReading);
            Assert.True(controller.IsPaused);
            Assert.Equal(0, controller.CurrentChunkIndex);
            Assert.True(speech.LastSpeakCancellation.IsCancellationRequested);

            await controller.ResumeAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await WaitForAsync(() => speech.SpokenTexts.Count >= 2, "the resumed chunk");

            Assert.False(controller.IsPaused);
            Assert.Equal(0, controller.CurrentChunkIndex);
            Assert.Equal("Opening section.", speech.SpokenTexts[1]);

            await controller.StopAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await read.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        }
        finally
        {
            await controller.DisposeAsync();
        }
    }

    /// <summary>
    /// Verifies stopping a long-form session cancels speech and reports reading end exactly once.
    /// </summary>
    [Fact]
    public async Task StopDuringLongFormCancelsSpeechAndRaisesReadingEvents()
    {
        var speech = new FakeSpeechOutputService { HoldPlayback = true };
        var controller = new NotesReadAloudController(
            speech,
            new FakeCallCoordinator(),
            new RecordingDiagnostics());
        var readingStates = new List<bool>();
        controller.IsReadingChanged += readingStates.Add;
        try
        {
            var read = controller.SpeakLongFormAsync(
                ["Only section one.", "Only section two."],
                null,
                CancellationToken.None);
            await WaitForAsync(() => speech.SpokenTexts.Count >= 1, "the first chunk");

            await controller.StopAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await read.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Assert.True(speech.StopCalls >= 1);
            Assert.True(speech.LastSpeakCancellation.IsCancellationRequested);
            Assert.False(controller.IsActive);
            Assert.False(controller.IsReading);
            Assert.False(controller.IsPaused);
            Assert.Equal([true, false], readingStates);
        }
        finally
        {
            await controller.DisposeAsync();
        }
    }

    /// <summary>
    /// Verifies the preferred voice is passed through when the service exposes it and clears again.
    /// </summary>
    [Fact]
    public async Task PreferredVoiceIdentifierIsPassedThroughWhenTheServiceExposesIt()
    {
        var speech = new FakeSpeechOutputService
        {
            AvailableVoices =
            [
                new CallVoice("voice-en-us", "English US", "en-US", true),
                new CallVoice("voice-en-gb", "English UK", "en-GB", false)
            ]
        };
        var controller = new NotesReadAloudController(
            speech,
            new FakeCallCoordinator(),
            new RecordingDiagnostics());
        try
        {
            controller.SetPreferredVoice("voice-en-gb");
            await controller.ReadAsync("Preferred voice passage.", null, CancellationToken.None);
            Assert.Equal("voice-en-gb", speech.LastVoiceName);

            controller.SetPreferredVoice(null);
            await controller.ReadAsync("Language fallback passage.", "en-US", CancellationToken.None);
            Assert.Equal("voice-en-us", speech.LastVoiceName);
        }
        finally
        {
            await controller.DisposeAsync();
        }
    }

    /// <summary>
    /// Waits until the condition holds so playback assertions stay free of fixed sleeps.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> condition, string description)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException("Timed out waiting for " + description);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Represents fake speech output service and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeSpeechOutputService : ISpeechOutputService
    {
        /// <summary>
        /// Gets or updates hold playback, the bindable or domain state represented by this property.
        /// </summary>
        public bool HoldPlayback { get; init; }
        /// <summary>
        /// Gets or updates speak calls, the bindable or domain state represented by this property.
        /// </summary>
        public int SpeakCalls { get; private set; }
        /// <summary>
        /// Gets every text passed to SpeakAsync in call order.
        /// </summary>
        public List<string> SpokenTexts { get; } = [];
        /// <summary>
        /// Gets or updates stop calls, the bindable or domain state represented by this property.
        /// </summary>
        public int StopCalls { get; private set; }
        /// <summary>
        /// Gets or updates last text, the bindable or domain state represented by this property.
        /// </summary>
        public string LastText { get; private set; } = string.Empty;
        /// <summary>
        /// Gets or updates last voice name, the bindable or domain state represented by this property.
        /// </summary>
        public string? LastVoiceName { get; private set; }
        /// <summary>
        /// Gets or updates last output device id, the bindable or domain state represented by this property.
        /// </summary>
        public string? LastOutputDeviceId { get; private set; }
        /// <summary>
        /// Gets or updates last speak cancellation, the bindable or domain state represented by this property.
        /// </summary>
        public CancellationToken LastSpeakCancellation { get; private set; }
        /// <summary>
        /// Gets or updates speak started, the bindable or domain state represented by this property.
        /// </summary>
        public TaskCompletionSource SpeakStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>
        /// Reports whether available applies to the current state.
        /// </summary>
        public bool IsAvailable => true;
        /// <summary>
        /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
        /// </summary>
        public string? UnavailableReason => null;
        /// <summary>
        /// Gets or updates available voices, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<CallVoice> AvailableVoices { get; init; } = [];
        /// <summary>
        /// Gets or updates voices, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<CallVoice> Voices => AvailableVoices;
        /// <summary>
        /// Gets or updates devices, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<CallAudioDevice> Devices { get; } =
            [new("default-output", "Default speakers", true)];

        /// <summary>
        /// Performs speak asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async Task SpeakAsync(
            string text,
            string? voiceName,
            string? outputDeviceId,
            CancellationToken cancellationToken)
        {
            SpeakCalls++;
            SpokenTexts.Add(text);
            LastText = text;
            LastVoiceName = voiceName;
            LastOutputDeviceId = outputDeviceId;
            LastSpeakCancellation = cancellationToken;
            SpeakStarted.TrySetResult();
            if (HoldPlayback)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

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
