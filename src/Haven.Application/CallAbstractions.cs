using Haven.Core;

namespace Haven.Application;

public sealed record CallStartOptions(
    ModelDescriptor Model,
    CallInputMode InputMode = CallInputMode.HandsFree,
    string? InputDeviceId = null,
    string? OutputDeviceId = null,
    string? VoiceName = null,
    bool EnableSpeechOutput = true,
    EffortLevel Effort = EffortLevel.Medium,
    string? SystemPrompt = null);

public sealed record CallCapabilities(
    bool HasSpeechInput,
    bool HasSpeechOutput,
    bool CanShareScreen,
    string? SpeechInputUnavailableReason,
    string? SpeechOutputUnavailableReason,
    string? ScreenShareUnavailableReason,
    IReadOnlyList<CallAudioDevice> InputDevices,
    IReadOnlyList<CallAudioDevice> OutputDevices,
    IReadOnlyList<CallVoice> Voices);

public enum SpeechInputEventKind
{
    SpeechStarted,
    PartialTranscript,
    FinalTranscript,
    AudioLevel,
    SpeechEnded,
    Error,
    SourceClosed
}

/// <summary>
/// A media adapter only exposes derived speech events. Raw samples remain inside
/// the adapter and must be discarded after transcription.
/// </summary>
public sealed record SpeechInputEvent(
    SpeechInputEventKind Kind,
    string? Text = null,
    double AudioLevel = 0,
    string? Error = null);

public sealed record SpeechInputOptions(
    string? DeviceId,
    SpeechModelInfo? Model,
    CallInputMode InputMode);

public sealed record ScreenShareSnapshot(
    string Base64Jpeg,
    int Width,
    int Height,
    DateTimeOffset CapturedAt);

public sealed record ScreenShareSource(string Id, string Name, bool IsWindow);

public sealed class ScreenShareSnapshotEventArgs(ScreenShareSnapshot snapshot) : EventArgs
{
    public ScreenShareSnapshot Snapshot { get; } = snapshot;
}

public sealed class CallStateChangedEventArgs(CallState state, string status) : EventArgs
{
    public CallState State { get; } = state;
    public string Status { get; } = status;
}

public sealed class CallTranscriptEventArgs(
    Guid messageId,
    MessageRole role,
    string text,
    bool isDelta,
    bool isFinal,
    bool wasInterrupted = false) : EventArgs
{
    public Guid MessageId { get; } = messageId;
    public MessageRole Role { get; } = role;
    public string Text { get; } = text;
    public bool IsDelta { get; } = isDelta;
    public bool IsFinal { get; } = isFinal;
    public bool WasInterrupted { get; } = wasInterrupted;
}

public sealed class CallAudioLevelEventArgs(double level) : EventArgs
{
    public double Level { get; } = level;
}

public interface ICallRepository
{
    Task UpsertAsync(CallSession session, CancellationToken cancellationToken);
    Task<CallSession?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<CallSession>> GetRecentAsync(int limit, CancellationToken cancellationToken);
}

public interface ISpeechInputService
{
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    IReadOnlyList<CallAudioDevice> Devices { get; }
    Task StartAsync(
        SpeechInputOptions options,
        Func<SpeechInputEvent, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken);
    Task BeginPushToTalkAsync(CancellationToken cancellationToken);
    Task EndPushToTalkAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface ISpeechOutputService
{
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    IReadOnlyList<CallAudioDevice> Devices { get; }
    IReadOnlyList<CallVoice> Voices { get; }
    Task SpeakAsync(string text, string? voiceName, string? outputDeviceId, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IScreenShareService
{
    bool IsSupported { get; }
    bool IsSharing { get; }
    string? UnavailableReason { get; }
    ScreenShareSource? CurrentSource { get; }
    event EventHandler? SourceClosed;
    event EventHandler<ScreenShareSnapshotEventArgs>? SnapshotAvailable;
    Task<ScreenShareSource> StartWithSystemPickerAsync(CancellationToken cancellationToken);
    Task<ScreenShareSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface ISpeechModelManager
{
    Task<IReadOnlyList<SpeechModelInfo>> GetModelsAsync(CancellationToken cancellationToken);
    Task<SpeechModelInfo> DownloadAsync(
        SpeechModelSize size,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
    Task DeleteAsync(SpeechModelSize size, CancellationToken cancellationToken);
}

public interface ICallCoordinator : IAsyncDisposable
{
    CallState State { get; }
    CallSession? CurrentSession { get; }
    Conversation? CurrentConversation { get; }
    CallCapabilities Capabilities { get; }
    bool IsActive { get; }
    bool IsMuted { get; }
    bool IsScreenSharing { get; }

    event EventHandler<CallStateChangedEventArgs>? StateChanged;
    event EventHandler<CallTranscriptEventArgs>? TranscriptChanged;
    event EventHandler<CallAudioLevelEventArgs>? AudioLevelChanged;
    event EventHandler<ScreenShareSnapshotEventArgs>? ScreenPreviewChanged;

    Task<CallSession> StartAsync(CallStartOptions options, SpeechModelInfo? speechModel, CancellationToken cancellationToken);
    Task SubmitTextAsync(string text, CancellationToken cancellationToken);
    Task BeginPushToTalkAsync(CancellationToken cancellationToken);
    Task EndPushToTalkAsync(CancellationToken cancellationToken);
    Task SetMutedAsync(bool muted, CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
    Task StartScreenShareAsync(CancellationToken cancellationToken);
    Task StopScreenShareAsync(CancellationToken cancellationToken);
    Task InterruptAsync(CancellationToken cancellationToken);
    Task EndAsync(CancellationToken cancellationToken);
}
