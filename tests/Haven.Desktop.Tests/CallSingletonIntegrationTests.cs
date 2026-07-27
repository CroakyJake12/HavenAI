/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/CallSingletonIntegrationTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns CallSingletonIntegrationTests, FakeSpeechOutput, RecordingDiagnostics. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents call singleton integration tests and keeps its related state and behavior together.
/// </summary>
public sealed class CallSingletonIntegrationTests
{
    /// <summary>
    /// Performs the desktop registration shares one speech output across call and preview step owned by this component.
    /// </summary>
    [Fact]
    public async Task DesktopRegistrationSharesOneSpeechOutputAcrossCallAndPreview()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProductionDiagnostics, RecordingDiagnostics>();
        services.AddHavenDesktopCallServices();
        await using var provider = services.BuildServiceProvider();

        var contract = provider.GetRequiredService<ISpeechOutputService>();
        var concrete = provider.GetRequiredService<HybridNaturalSpeechOutputService>();
        var windowsFallback = provider.GetRequiredService<WindowsNaturalSpeechOutputService>();
        var preview = provider.GetRequiredService<CallVoicePreviewController>();

        Assert.Same(concrete, contract);
        Assert.NotSame(windowsFallback, contract);
        Assert.Contains(contract.Voices, voice => voice.Id.StartsWith("kokoro:", StringComparison.Ordinal));
        Assert.Same(contract, provider.GetRequiredService<ISpeechOutputService>());
        Assert.Same(preview, provider.GetRequiredService<CallVoicePreviewController>());
    }

    /// <summary>
    /// Performs the preview uses selected voice and fixed local text step owned by this component.
    /// </summary>
    [Fact]
    public async Task PreviewUsesSelectedVoiceAndFixedLocalText()
    {
        var output = new FakeSpeechOutput();
        var diagnostics = new RecordingDiagnostics();
        await using var controller = new CallVoicePreviewController(output, diagnostics);
        var voice = new CallVoice("voice-id", "Test voice", "en-GB", true);

        await controller.PreviewAsync(voice, "default", CancellationToken.None);

        Assert.Equal("voice-id", output.LastVoice);
        Assert.Equal("default", output.LastDevice);
        Assert.Contains("this is Haven", output.LastText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(diagnostics.Events, item => item.EventName == "voice-preview-started");
        Assert.Contains(diagnostics.Events, item => item.EventName == "voice-preview-completed");
    }

    /// <summary>
    /// Performs the stop cancels an active preview without waiting for playback step owned by this component.
    /// </summary>
    [Fact]
    public async Task StopCancelsAnActivePreviewWithoutWaitingForPlayback()
    {
        var output = new FakeSpeechOutput { BlockPlayback = true };
        var diagnostics = new RecordingDiagnostics();
        await using var controller = new CallVoicePreviewController(output, diagnostics);
        var preview = controller.PreviewAsync(
            new CallVoice("voice-id", "Test voice", "en-GB", true),
            "default",
            CancellationToken.None);
        await output.Started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        await controller.StopAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preview);
        Assert.True(output.StopCount > 0);
        Assert.Contains(diagnostics.Events, item => item.EventName == "voice-preview-cancelled");
    }

    /// <summary>
    /// Performs the unavailable speech fails before playback and reports nothing sensitive step owned by this component.
    /// </summary>
    [Fact]
    public async Task UnavailableSpeechFailsBeforePlaybackAndReportsNothingSensitive()
    {
        var output = new FakeSpeechOutput { IsAvailable = false, UnavailableReason = "No voices installed." };
        var diagnostics = new RecordingDiagnostics();
        await using var controller = new CallVoicePreviewController(output, diagnostics);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => controller.PreviewAsync(
            new CallVoice("voice-id", "Test voice", "en-GB", true),
            "default",
            CancellationToken.None));

        Assert.Contains("No voices", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(output.LastText);
        Assert.Empty(diagnostics.Events);
    }

    /// <summary>
    /// Represents fake speech output and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeSpeechOutput : ISpeechOutputService
    {
        /// <summary>
        /// Reports whether available applies to the current state.
        /// </summary>
        public bool IsAvailable { get; set; } = true;
        /// <summary>
        /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
        /// </summary>
        public string? UnavailableReason { get; set; }
        /// <summary>
        /// Gets or updates devices, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<CallAudioDevice> Devices { get; } = [new("default", "Default", true)];
        /// <summary>
        /// Gets or updates voices, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<CallVoice> Voices { get; } = [new("voice-id", "Test voice", "en-GB", true)];
        /// <summary>
        /// Gets or updates last text, the bindable or domain state represented by this property.
        /// </summary>
        public string? LastText { get; private set; }
        /// <summary>
        /// Gets or updates last voice, the bindable or domain state represented by this property.
        /// </summary>
        public string? LastVoice { get; private set; }
        /// <summary>
        /// Gets or updates last device, the bindable or domain state represented by this property.
        /// </summary>
        public string? LastDevice { get; private set; }
        /// <summary>
        /// Gets or updates block playback, the bindable or domain state represented by this property.
        /// </summary>
        public bool BlockPlayback { get; set; }
        /// <summary>
        /// Gets or updates stop count, the bindable or domain state represented by this property.
        /// </summary>
        public int StopCount { get; private set; }
        /// <summary>
        /// Gets or updates started, the bindable or domain state represented by this property.
        /// </summary>
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Performs speak asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async Task SpeakAsync(string text, string? voiceName, string? outputDeviceId, CancellationToken cancellationToken)
        {
            LastText = text;
            LastVoice = voiceName;
            LastDevice = outputDeviceId;
            Started.TrySetResult();
            if (BlockPlayback) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        /// <summary>
        /// Performs stop asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
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
        public List<ReliabilityEvent> Events { get; } = [];

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
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(new ReliabilityEvent(
                DateTimeOffset.UtcNow,
                severity,
                component,
                eventName,
                message,
                correlationId ?? string.Empty,
                data ?? new Dictionary<string, string>()));
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Performs read recent asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReliabilityEvent>>(Events.Take(limit).ToArray());

        /// <summary>
        /// Performs dispose asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
