using Haven.Core;

namespace Haven.Application;

/// <summary>Bounded delivery hints for local speech output.</summary>
public sealed record VoiceDeliveryStyle(
    string Label,
    float Pace,
    float Energy,
    float Emphasis)
{
    public static VoiceDeliveryStyle Conversational { get; } = new("Conversational", 1.00f, 0.50f, 0.45f);
}

/// <summary>
/// Optional speech capability for engines that can express delivery differences.
/// The ordinary ISpeechOutputService contract remains unchanged for fallbacks.
/// </summary>
public interface IAdaptiveSpeechOutputService
{
    Task SpeakAsync(
        string text,
        string? voiceName,
        string? outputDeviceId,
        VoiceDeliveryStyle style,
        CancellationToken cancellationToken);
}

/// <summary>Deterministic mode/reaction policy for speech delivery.</summary>
public static class VoiceDeliveryStylePolicy
{
    public static VoiceDeliveryStyle Resolve(VoiceProfile? profile, VoiceReaction? reaction, string text)
    {
        var style = profile?.Id.ToLowerInvariant() switch
        {
            "lesson" => new VoiceDeliveryStyle("Coach", 0.93f, 0.46f, 0.68f),
            "planning" => new VoiceDeliveryStyle("Deliberate", 0.95f, 0.36f, 0.56f),
            "development" => new VoiceDeliveryStyle("Focused", 0.98f, 0.38f, 0.58f),
            "news-reader" => new VoiceDeliveryStyle("Broadcast", 1.03f, 0.58f, 0.66f),
            "commentator" => new VoiceDeliveryStyle("Commentary", 1.11f, 0.82f, 0.78f),
            _ => VoiceDeliveryStyle.Conversational
        };

        if (reaction?.LessonPhase is LessonVoicePhase.KnowledgeCheck or LessonVoicePhase.Review)
            style = style with { Pace = style.Pace - 0.03f, Emphasis = Math.Max(style.Emphasis, 0.72f) };
        else if (reaction?.Kind == VoiceReactionKind.ActionExecuted)
            style = style with { Pace = style.Pace + 0.03f, Energy = Math.Max(style.Energy, 0.66f) };

        var trimmed = text.Trim();
        if (trimmed.EndsWith('?')) style = style with { Pace = style.Pace - 0.02f };
        if (trimmed.EndsWith('!')) style = style with { Pace = style.Pace + 0.02f, Energy = Math.Min(1, style.Energy + 0.08f) };

        return style with
        {
            Pace = Math.Clamp(style.Pace, 0.88f, 1.16f),
            Energy = Math.Clamp(style.Energy, 0, 1),
            Emphasis = Math.Clamp(style.Emphasis, 0, 1)
        };
    }
}
