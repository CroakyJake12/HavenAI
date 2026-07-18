/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/CallFallbackServices.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns UnavailableSpeechInputService, UnsupportedScreenShareService, SystemSpeechOutputService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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
    /// <summary>
    /// Reports whether is available is true for the current state.
    /// </summary>
    public bool IsAvailable => false;
    /// <summary>
    /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
    /// </summary>
    public string UnavailableReason =>
        "Local microphone transcription is not installed. Use typed transcript mode or install the Haven speech runtime.";
    /// <summary>
    /// Gets or updates devices, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<CallAudioDevice> Devices { get; } = [];

    /// <summary>
    /// Performs start async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task StartAsync(
        SpeechInputOptions options,
        Func<SpeechInputEvent, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Performs begin push to talk async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task BeginPushToTalkAsync(CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException(UnavailableReason));

    /// <summary>
    /// Performs end push to talk async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task EndPushToTalkAsync(CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException(UnavailableReason));

    /// <summary>
    /// Performs stop async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Capability-gated fallback. A Windows Graphics Capture implementation can
/// replace it without changing the coordinator or UI.
/// </summary>
public sealed class UnsupportedScreenShareService : IScreenShareService
{
    /// <summary>
    /// Reports whether is supported is true for the current state.
    /// </summary>
    public bool IsSupported => false;
    /// <summary>
    /// Reports whether is sharing is true for the current state.
    /// </summary>
    public bool IsSharing => false;
    /// <summary>
    /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
    /// </summary>
    public string UnavailableReason =>
        OperatingSystem.IsWindows()
            ? "The Windows screen-capture adapter is not installed."
            : "Screen sharing currently requires Windows.";
    /// <summary>
    /// Gets or updates current source, the bindable or domain state represented by this property.
    /// </summary>
    public ScreenShareSource? CurrentSource => null;
    /// <summary>
    /// Gets or updates source closed, the bindable or domain state represented by this property.
    /// </summary>
    public event EventHandler? SourceClosed { add { } remove { } }
    /// <summary>
    /// Gets or updates snapshot available, the bindable or domain state represented by this property.
    /// </summary>
    public event EventHandler<ScreenShareSnapshotEventArgs>? SnapshotAvailable { add { } remove { } }

    /// <summary>
    /// Performs start with system picker async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<ScreenShareSource> StartWithSystemPickerAsync(CancellationToken cancellationToken) =>
        Task.FromException<ScreenShareSource>(new PlatformNotSupportedException(UnavailableReason));

    /// <summary>
    /// Retrieves latest snapshot async for the current operation.
    /// </summary>
    public Task<ScreenShareSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken) =>
        Task.FromResult<ScreenShareSnapshot?>(null);

    /// <summary>
    /// Performs stop async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Local Windows text-to-speech with prompt-level cancellation for barge-in.
/// </summary>
public sealed class SystemSpeechOutputService : ISpeechOutputService
{
    /// <summary>
    /// Stores sync locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly object _sync = new();
    /// <summary>
    /// Stores active synthesizer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private SpeechSynthesizer? _activeSynthesizer;

    /// <summary>
    /// Reports whether is available is true for the current state.
    /// </summary>
    public bool IsAvailable => OperatingSystem.IsWindows();
    /// <summary>
    /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
    /// </summary>
    public string? UnavailableReason => IsAvailable
        ? null
        : "Speech output currently requires Windows.";

    /// <summary>
    /// Gets or updates devices, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<CallAudioDevice> Devices { get; } =
        [new("default", "System default output", true)];

    /// <summary>
    /// Gets or updates voices, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<CallVoice> Voices => OperatingSystem.IsWindows() ? ReadVoices() : [];

    /// <summary>
    /// Performs speak async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs stop async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows()) StopActiveSynthesizer();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs the stop active synthesizer step owned by this component.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private void StopActiveSynthesizer()
    {
        SpeechSynthesizer? synthesizer;
        lock (_sync) synthesizer = _activeSynthesizer;
        if (synthesizer is null) return;
        try { synthesizer.SpeakAsyncCancelAll(); }
        catch (Exception) { /* a concurrent completion already disposed it */ }
    }

    /// <summary>
    /// Performs the read voices step owned by this component.
    /// </summary>
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
