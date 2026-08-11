using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Discovers first-party and user-created Voice Profiles. Profiles configure
/// the trusted Voice runtime; they do not grant capabilities or bypass policy.
/// </summary>
public sealed class VoiceProfileCatalog
{
    private readonly object _gate = new();
    private readonly Dictionary<string, VoiceProfile> _profiles =
        BuiltIns.ToDictionary(profile => profile.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<VoiceProfile> BuiltIns { get; } =
    [
        new(
            "general",
            "General Voice",
            "Natural local voice conversation.",
            "Respond naturally and concisely for speech. Keep answers clear and conversational.",
            ContinuousListening: true),
        new(
            "lesson",
            "Lesson Voice",
            "Follow a lesson and preserve structured activity context.",
            "Act as a live lesson companion. Track lesson phase, starter tasks, explanations, marking guidance, notes and homework. Never invent unseen board or document content. Treat ambiguous classroom speech as uncertain and do not perform consequential actions without the normal permission policy.",
            ContinuousListening: true),
        new(
            "planning",
            "Planning Voice",
            "Turn spoken goals into structured plans and reminders.",
            "Help organise spoken goals into clear plans. Ask for missing dates or ambiguity before creating consequential reminders.",
            AllowAutomaticActions: false),
        new(
            "development",
            "Development Voice",
            "Support spoken development and debugging work.",
            "Help inspect, explain and validate development work. Use Studio and Tasks capabilities through their normal permission and workspace boundaries. Keep spoken debugging steps compact and announce the next concrete check before expanding.",
            AllowAutomaticActions: false),
        new(
            "news-reader",
            "News Reader",
            "Read and explain source-backed material with broadcast-style pacing.",
            "Sound like a calm local news reader: lead with the key fact, distinguish source text from inference, use short transitions, and avoid conversational filler. When Browse is explicitly requested, use the normal trusted Browse route; never invent unseen source details.",
            ContinuousListening: true,
            AllowAutomaticActions: true,
            RetainTranscript: false),
        new(
            "commentator",
            "Commentator",
            "React live to an ongoing activity with faster, higher-energy delivery.",
            "Act as a concise live commentator. React to meaningful changes instead of narrating every second, use energetic but controlled phrasing, avoid repeating the same observation, and never claim to see or know activity that has not arrived through current Voice or screen context.",
            ContinuousListening: true,
            AllowAutomaticActions: false,
            RetainTranscript: false)
    ];

    public IReadOnlyList<VoiceProfile> GetAll()
    {
        lock (_gate) return _profiles.Values.Where(profile => profile.IsEnabled).OrderBy(profile => profile.Name).ToArray();
    }

    public VoiceProfile? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate) return _profiles.TryGetValue(id, out var profile) && profile.IsEnabled ? profile : null;
    }

    public VoiceProfile UpsertUserProfile(VoiceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name))
            throw new ArgumentException("A Voice Profile requires an ID and name.", nameof(profile));
        lock (_gate)
        {
            var userProfile = profile with { IsBuiltIn = false };
            _profiles[userProfile.Id] = userProfile;
            return userProfile;
        }
    }

    public bool RemoveUserProfile(string id)
    {
        lock (_gate)
        {
            return _profiles.TryGetValue(id, out var profile)
                && !profile.IsBuiltIn
                && _profiles.Remove(id);
        }
    }
}
