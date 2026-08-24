namespace Haven.Core;

/// <summary>Scoped configuration for a live Voice experience.</summary>
public sealed record VoiceProfile(
    string Id,
    string Name,
    string Description,
    string Instructions,
    bool RequiresRealtimeProcessing = true,
    bool ContinuousListening = false,
    bool AllowAutomaticActions = false,
    bool RetainTranscript = false,
    bool IsBuiltIn = true,
    bool IsEnabled = true);
