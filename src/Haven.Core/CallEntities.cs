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

public enum CallInputMode
{
    HandsFree = 0,
    PushToTalk = 1
}

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

public sealed record CallAudioDevice(string Id, string Name, bool IsDefault = false);
public sealed record CallVoice(string Id, string Name, string? Culture = null, bool IsDefault = false);

public enum SpeechModelSize { Tiny = 0, Base = 1, Small = 2 }

public sealed record SpeechModelInfo(
    SpeechModelSize Size,
    string DisplayName,
    string FileName,
    long ApproximateSizeBytes,
    bool IsInstalled,
    string LocalPath);
