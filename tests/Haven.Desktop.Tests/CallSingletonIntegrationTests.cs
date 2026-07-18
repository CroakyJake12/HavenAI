using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class CallSingletonIntegrationTests
{
    [Fact]
    public async Task DesktopRegistrationSharesOneSpeechOutputAcrossCallAndPreview()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProductionDiagnostics, RecordingDiagnostics>();
        services.AddHavenDesktopCallServices();
        await using var provider = services.BuildServiceProvider();

        var contract = provider.GetRequiredService<ISpeechOutputService>();
        var concrete = provider.GetRequiredService<WindowsNaturalSpeechOutputService>();
        var preview = provider.GetRequiredService<CallVoicePreviewController>();

        Assert.Same(concrete, contract);
        Assert.Same(contract, provider.GetRequiredService<ISpeechOutputService>());
        Assert.Same(preview, provider.GetRequiredService<CallVoicePreviewController>());
    }

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

    private sealed class FakeSpeechOutput : ISpeechOutputService
    {
        public bool IsAvailable { get; set; } = true;
        public string? UnavailableReason { get; set; }
        public IReadOnlyList<CallAudioDevice> Devices { get; } = [new("default", "Default", true)];
        public IReadOnlyList<CallVoice> Voices { get; } = [new("voice-id", "Test voice", "en-GB", true)];
        public string? LastText { get; private set; }
        public string? LastVoice { get; private set; }
        public string? LastDevice { get; private set; }
        public bool BlockPlayback { get; set; }
        public int StopCount { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SpeakAsync(string text, string? voiceName, string? outputDeviceId, CancellationToken cancellationToken)
        {
            LastText = text;
            LastVoice = voiceName;
            LastDevice = outputDeviceId;
            Started.TrySetResult();
            if (BlockPlayback) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDiagnostics : IProductionDiagnostics
    {
        public List<ReliabilityEvent> Events { get; } = [];

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

        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReliabilityEvent>>(Events.Take(limit).ToArray());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
