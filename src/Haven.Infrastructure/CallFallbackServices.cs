using System.Speech.Synthesis;
using System.Runtime.Versioning;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Safe default until the optional NAudio/Whisper runtime is installed. The Call
/// coordinator remains fully usable through typed transcript input.
/// </summary>
public sealed class UnavailableSpeechInputService : ISpeechInputService
{
    public bool IsAvailable => false;
    public string UnavailableReason =>
        "Local microphone transcription is not installed. Use typed transcript mode or install the Haven speech runtime.";
    public IReadOnlyList<CallAudioDevice> Devices { get; } = [];

    public Task StartAsync(
        SpeechInputOptions options,
        Func<SpeechInputEvent, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task BeginPushToTalkAsync(CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException(UnavailableReason));

    public Task EndPushToTalkAsync(CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException(UnavailableReason));

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Capability-gated fallback. A Windows Graphics Capture implementation can
/// replace it without changing the coordinator or UI.
/// </summary>
public sealed class UnsupportedScreenShareService : IScreenShareService
{
    public bool IsSupported => false;
    public bool IsSharing => false;
    public string UnavailableReason =>
        OperatingSystem.IsWindows()
            ? "The Windows screen-capture adapter is not installed."
            : "Screen sharing currently requires Windows.";
    public ScreenShareSource? CurrentSource => null;
    public event EventHandler? SourceClosed { add { } remove { } }
    public event EventHandler<ScreenShareSnapshotEventArgs>? SnapshotAvailable { add { } remove { } }

    public Task<ScreenShareSource> StartWithSystemPickerAsync(CancellationToken cancellationToken) =>
        Task.FromException<ScreenShareSource>(new PlatformNotSupportedException(UnavailableReason));

    public Task<ScreenShareSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken) =>
        Task.FromResult<ScreenShareSnapshot?>(null);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Local Windows text-to-speech with prompt-level cancellation for barge-in.
/// </summary>
public sealed class SystemSpeechOutputService : ISpeechOutputService
{
    private readonly object _sync = new();
    private SpeechSynthesizer? _activeSynthesizer;

    public bool IsAvailable => OperatingSystem.IsWindows();
    public string? UnavailableReason => IsAvailable
        ? null
        : "Speech output currently requires Windows.";

    public IReadOnlyList<CallAudioDevice> Devices { get; } =
        [new("default", "System default output", true)];

    public IReadOnlyList<CallVoice> Voices => OperatingSystem.IsWindows() ? ReadVoices() : [];

    public async Task SpeakAsync(
        string text,
        string? voiceName,
        string? outputDeviceId,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(UnavailableReason);
        if (string.IsNullOrWhiteSpace(text)) return;

        cancellationToken.ThrowIfCancellationRequested();
        using var synthesizer = new SpeechSynthesizer();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<SpeakCompletedEventArgs>? completed = null;
        completed = (_, args) =>
        {
            if (args.Error is not null) completion.TrySetException(args.Error);
            else if (args.Cancelled) completion.TrySetCanceled();
            else completion.TrySetResult();
        };
        synthesizer.SpeakCompleted += completed;
        try
        {
            if (!string.IsNullOrWhiteSpace(voiceName))
                synthesizer.SelectVoice(voiceName);

            using var registration = cancellationToken.Register(StopActiveSynthesizer);
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _activeSynthesizer = synthesizer;
                synthesizer.SpeakAsync(text);
            }
            await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            synthesizer.SpeakCompleted -= completed;
            lock (_sync)
            {
                if (ReferenceEquals(_activeSynthesizer, synthesizer)) _activeSynthesizer = null;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows()) StopActiveSynthesizer();
        return Task.CompletedTask;
    }

    [SupportedOSPlatform("windows")]
    private void StopActiveSynthesizer()
    {
        SpeechSynthesizer? synthesizer;
        lock (_sync) synthesizer = _activeSynthesizer;
        if (synthesizer is null) return;
        try { synthesizer.SpeakAsyncCancelAll(); }
        catch (Exception) { /* a concurrent completion already disposed it */ }
    }

    [SupportedOSPlatform("windows")]
    private IReadOnlyList<CallVoice> ReadVoices()
    {
        if (!OperatingSystem.IsWindows()) return [];
        try
        {
            using var synthesizer = new SpeechSynthesizer();
            var result = synthesizer.GetInstalledVoices()
                .Where(voice => voice.Enabled)
                .Select((voice, index) => new CallVoice(
                    voice.VoiceInfo.Name,
                    voice.VoiceInfo.Name,
                    voice.VoiceInfo.Culture.Name,
                    index == 0))
                .ToList();
            return result.Count > 0 ? result : [new("default", "System default voice", null, true)];
        }
        catch (Exception)
        {
            return [new("default", "System default voice", null, true)];
        }
    }
}
