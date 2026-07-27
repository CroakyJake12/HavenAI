// Call session state, audio devices, voices, and speech model info.

namespace Haven.Core;

/// <summary>
/// The externally observable states of the local Call coordinator.
/// Values are explicit because the state can be written to diagnostics.
/// </summary>
public enum CallState
{
    Idle = 0,
    Listening = 1,
    Transcribing = 2,
    Thinking = 3,
    Speaking = 4,
    Paused = 5,
    Error = 6
}

/// <summary>
/// Lists the supported call input mode values used to make state explicit and type-safe.
/// </summary>
public enum CallInputMode
{
    HandsFree = 0,
    PushToTalk = 1
}

/// <summary>
/// Lists the supported call session status values used to make state explicit and type-safe.
/// </summary>
public enum CallSessionStatus
{
    Active = 0,
    Completed = 1,
    Cancelled = 2,
    Failed = 3
}

/// <summary>
/// Durable metadata for a call. Audio samples and screen frames are deliberately
/// absent; the transcript is stored as ordinary conversation messages.
/// </summary>
public sealed record CallSession(
    Guid Id,
    Guid ConversationId,
    string ModelName,
    string? InputDeviceId,
    string? OutputDeviceId,
    string? VoiceName,
    CallInputMode InputMode,
    bool UsedScreenShare,
    CallSessionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt = null,
    string? Error = null);

/// <summary>
/// Represents call audio device and keeps its related state and behavior together.
/// </summary>
public sealed record CallAudioDevice(string Id, string Name, bool IsDefault = false);
/// <summary>
/// Represents call voice and keeps its related state and behavior together.
/// </summary>
public sealed record CallVoice(string Id, string Name, string? Culture = null, bool IsDefault = false);

/// <summary>
/// Lists the supported speech model size values used to make state explicit and type-safe.
/// </summary>
public enum SpeechModelSize { Tiny = 0, Base = 1, Small = 2 }

/// <summary>
/// Represents speech model info and keeps its related state and behavior together.
/// </summary>
public sealed record SpeechModelInfo(
    SpeechModelSize Size,
    string DisplayName,
    string FileName,
    long ApproximateSizeBytes,
    bool IsInstalled,
    string LocalPath);
