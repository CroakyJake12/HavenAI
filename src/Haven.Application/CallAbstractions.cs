/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/CallAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns CallStartOptions, CallCapabilities, SpeechInputEventKind, SpeechInputEvent, SpeechInputOptions, ScreenShareSnapshot, ScreenShareSource, ScreenShareSnapshotEventArgs, CallStateChangedEventArgs, CallTranscriptEventArgs, CallAudioLevelEventArgs, ICallRepository, ISpeechInputService, ISpeechOutputService, IScreenShareService, ISpeechModelManager, ICallCoordinator. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents call start options and keeps its related state and behavior together.
/// </summary>
public sealed record CallStartOptions(
    ModelDescriptor Model,
    CallInputMode InputMode = CallInputMode.HandsFree,
    string? InputDeviceId = null,
    string? OutputDeviceId = null,
    string? VoiceName = null,
    bool EnableSpeechOutput = true,
    EffortLevel Effort = EffortLevel.Low,
    string? SystemPrompt =
        "You are Haven in a private, local live call. Respond promptly, warmly and conversationally. " +
        "Use contractions, varied sentence rhythm, and brief natural acknowledgements so the voice sounds expressive rather than scripted. " +
        "When a reply genuinely needs thought, begin with one very short cue such as â€˜Hmmâ€¦â€™ or â€˜Rightâ€¦â€™, then move directly into the answer; do not use a cue on every turn. " +
        "Prefer short spoken sentences and avoid headings or markdown unless the user requests them. " +
        "Do not claim to see a shared screen unless an image is attached to the current turn.",
    string? VoiceProfileId = null,
    VoiceProfile? VoiceProfile = null);

/// <summary>
/// Represents call capabilities and keeps its related state and behavior together.
/// </summary>
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

/// <summary>
/// Lists the supported speech input event kind values used to make state explicit and type-safe.
/// </summary>
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

/// <summary>
/// Represents speech input options and keeps its related state and behavior together.
/// </summary>
public sealed record SpeechInputOptions(
    string? DeviceId,
    SpeechModelInfo? Model,
    CallInputMode InputMode);

/// <summary>
/// Represents screen share snapshot and keeps its related state and behavior together.
/// </summary>
public sealed record ScreenShareSnapshot(
    string Base64Jpeg,
    int Width,
    int Height,
    DateTimeOffset CapturedAt);

/// <summary>
/// Identifies whether a capture source is a window, a screen, or could not be classified truthfully.
/// </summary>
public enum ScreenShareSourceKind
{
    Unknown = 0,
    Window = 1,
    Screen = 2
}
/// <summary>
/// Represents a user-selected screen-share source and its verified classification when available.
/// </summary>
public sealed record ScreenShareSource(string Id, string Name, ScreenShareSourceKind Kind);

/// <summary>
/// Represents screen share snapshot event args and keeps its related state and behavior together.
/// </summary>
public sealed class ScreenShareSnapshotEventArgs(ScreenShareSnapshot snapshot) : EventArgs
{
    /// <summary>
    /// Gets or updates snapshot, the bindable or domain state represented by this property.
    /// </summary>
    public ScreenShareSnapshot Snapshot { get; } = snapshot;
}

/// <summary>
/// Represents call state changed event args and keeps its related state and behavior together.
/// </summary>
public sealed class CallStateChangedEventArgs(CallState state, string status) : EventArgs
{
    /// <summary>
    /// Gets or updates state, the bindable or domain state represented by this property.
    /// </summary>
    public CallState State { get; } = state;
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get; } = status;
}

/// <summary>
/// Represents call transcript event args and keeps its related state and behavior together.
/// </summary>
public sealed class CallTranscriptEventArgs(
    Guid messageId,
    MessageRole role,
    string text,
    bool isDelta,
    bool isFinal,
    bool wasInterrupted = false) : EventArgs
{
    /// <summary>
    /// Gets or updates message id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid MessageId { get; } = messageId;
    /// <summary>
    /// Gets or updates role, the bindable or domain state represented by this property.
    /// </summary>
    public MessageRole Role { get; } = role;
    /// <summary>
    /// Gets or updates text, the bindable or domain state represented by this property.
    /// </summary>
    public string Text { get; } = text;
    /// <summary>
    /// Reports whether delta applies to the current state.
    /// </summary>
    public bool IsDelta { get; } = isDelta;
    /// <summary>
    /// Reports whether final applies to the current state.
    /// </summary>
    public bool IsFinal { get; } = isFinal;
    /// <summary>
    /// Gets or updates was interrupted, the bindable or domain state represented by this property.
    /// </summary>
    public bool WasInterrupted { get; } = wasInterrupted;
}

/// <summary>
/// Represents call audio level event args and keeps its related state and behavior together.
/// </summary>
public sealed class CallAudioLevelEventArgs(double level) : EventArgs
{
    /// <summary>
    /// Gets or updates level, the bindable or domain state represented by this property.
    /// </summary>
    public double Level { get; } = level;
}

/// <summary>
/// Truthful runtime state for Voice microphone capture. This is separate from the broader
/// call state because a Voice session can remain active in typed-transcript fallback mode.
/// </summary>
public enum VoiceInputState
{
    Ready = 0,
    Starting = 1,
    Listening = 2,
    Muted = 3,
    Paused = 4,
    PermissionDenied = 5,
    Unavailable = 6,
    Error = 7
}

public sealed record VoiceInputStatus(
    VoiceInputState State,
    string Message,
    bool CanRetry = false);

public sealed class VoiceInputStatusChangedEventArgs(VoiceInputStatus status) : EventArgs
{
    public VoiceInputStatus Status { get; } = status;
}

/// <summary>
/// Optional runtime Voice-input status exposed by coordinators that can report capture
/// degradation independently from the active conversation/session state.
/// </summary>
public interface IVoiceInputStatusSource
{
    VoiceInputStatus InputStatus { get; }
    event EventHandler<VoiceInputStatusChangedEventArgs>? InputStatusChanged;
}

/// <summary>
/// Defines the call repository contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface ICallRepository
{
    Task UpsertAsync(CallSession session, CancellationToken cancellationToken);
    Task<CallSession?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<CallSession>> GetRecentAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the speech input service contract so callers depend on a capability rather than one implementation.
/// </summary>
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

/// <summary>
/// Defines the speech output service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface ISpeechOutputService
{
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    IReadOnlyList<CallAudioDevice> Devices { get; }
    IReadOnlyList<CallVoice> Voices { get; }
    Task SpeakAsync(string text, string? voiceName, string? outputDeviceId, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Defines the screen share service contract so callers depend on a capability rather than one implementation.
/// </summary>
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

/// <summary>
/// Defines the speech model manager contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface ISpeechModelManager
{
    Task<IReadOnlyList<SpeechModelInfo>> GetModelsAsync(CancellationToken cancellationToken);
    Task<SpeechModelInfo> DownloadAsync(
        SpeechModelSize size,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
    Task DeleteAsync(SpeechModelSize size, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the call coordinator contract so callers depend on a capability rather than one implementation.
/// </summary>
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
