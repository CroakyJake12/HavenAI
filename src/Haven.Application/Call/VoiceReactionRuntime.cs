using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>High-level categories emitted by the live Voice reaction runtime.</summary>
public enum VoiceReactionKind
{
    Listening,
    PartialUnderstanding,
    FinalUnderstanding,
    LessonPhaseChanged,
    ActionSuggested,
    ActionExecuted,
    ScreenContextChanged
}

/// <summary>Structured phase retained only for the lifetime of a Lesson Voice call.</summary>
public enum LessonVoicePhase
{
    None,
    Starter,
    Explanation,
    GuidedPractice,
    KnowledgeCheck,
    Review,
    Homework,
    Resource
}

/// <summary>A bounded semantic action inferred from live speech.</summary>
public sealed record VoiceReactionAction(
    string Id,
    HavenSurface TargetSurface,
    string Intent,
    string? Query,
    double Confidence,
    bool RequiresConfirmation,
    bool WasAutoExecuted = false);

/// <summary>
/// Ephemeral live reaction state. It deliberately carries summaries and structured
/// intent instead of raw audio. Transcript text remains in the current call turn only.
/// </summary>
public sealed record VoiceReaction(
    long Sequence,
    VoiceReactionKind Kind,
    string Summary,
    double Confidence,
    DateTimeOffset CreatedAt,
    LessonVoicePhase LessonPhase = LessonVoicePhase.None,
    VoiceReactionAction? Action = null,
    bool IsPartial = false);

public sealed class VoiceReactionEventArgs(VoiceReaction reaction) : EventArgs
{
    public VoiceReaction Reaction { get; } = reaction;
}

/// <summary>Routes only safe, already-policy-approved automatic Voice reactions.</summary>
public interface IVoiceReactionActionRouter
{
    Task<bool> RouteAsync(VoiceReactionAction action, CancellationToken cancellationToken);
}

/// <summary>
/// Uses the existing surface orchestration path so Voice does not invent a parallel
/// navigation/action system. Consequential actions never reach this router automatically.
/// </summary>
public sealed class SurfaceVoiceReactionActionRouter(SurfaceOrchestrationService surfaces) : IVoiceReactionActionRouter
{
    public async Task<bool> RouteAsync(VoiceReactionAction action, CancellationToken cancellationToken)
    {
        if (action.RequiresConfirmation || action.Confidence < 0.92) return false;
        if (action.TargetSurface is not (HavenSurface.Browse or HavenSurface.Study or HavenSurface.Plan)) return false;

        var targetSurface = action.TargetSurface switch
        {
            HavenSurface.Study => SurfaceKind.Study,
            HavenSurface.Browse => SurfaceKind.Browse,
            HavenSurface.Plan => SurfaceKind.Plan,
            _ => SurfaceKind.Chat
        };
        var currentMode = action.TargetSurface switch
        {
            HavenSurface.Study => HavenMode.Study,
            HavenSurface.Plan => HavenMode.Tasks,
            _ => HavenMode.Chat
        };
        var resolution = await surfaces.ResolveAsync(
            action.Query ?? action.Intent,
            currentMode,
            workspaceRoot: null,
            cancellationToken).ConfigureAwait(false);
        return resolution.TargetSurface == targetSurface;
    }
}

/// <summary>
/// Deterministic, bounded classifier for live Voice reactions. It owns sequencing,
/// duplicate suppression and Lesson Voice phase continuity, while model generation
/// remains responsible for natural-language answers.
/// </summary>
public sealed class VoiceReactionRuntime
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PartialUpdateInterval = TimeSpan.FromMilliseconds(450);
    private readonly VoiceProfile _profile;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, DateTimeOffset> _recent = new(StringComparer.Ordinal);
    private long _sequence;
    private string _lastPartial = string.Empty;
    private DateTimeOffset _lastPartialPublishedAt = DateTimeOffset.MinValue;
    private LessonVoicePhase _lastPartialPhase = LessonVoicePhase.None;

    public VoiceReactionRuntime(VoiceProfile profile, TimeProvider? timeProvider = null)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public LessonVoicePhase LessonPhase { get; private set; }
    public VoiceReaction? Current { get; private set; }

    public VoiceReaction? ObserveSpeechStarted()
        => Publish(VoiceReactionKind.Listening, "Listening live", 1, LessonPhase, null, isPartial: true, "speech-started");

    public VoiceReaction? ObservePartial(string text)
    {
        if (!_profile.RequiresRealtimeProcessing) return null;
        var normalized = Normalize(text);
        if (normalized.Length < 6 || normalized.Equals(_lastPartial, StringComparison.Ordinal)) return null;
        _lastPartial = normalized;

        var phase = _profile.Id.Equals("lesson", StringComparison.OrdinalIgnoreCase)
            ? DetectLessonPhase(normalized)
            : LessonVoicePhase.None;
        var now = _timeProvider.GetUtcNow();
        var phaseChanged = phase != LessonVoicePhase.None && phase != _lastPartialPhase;
        if (!phaseChanged && now - _lastPartialPublishedAt < PartialUpdateInterval) return null;
        _lastPartialPublishedAt = now;
        _lastPartialPhase = phase;

        var summary = phase != LessonVoicePhase.None
            ? $"Following {DescribePhase(phase).ToLowerInvariant()}"
            : "Understanding live speech";
        return Publish(
            phase != LessonVoicePhase.None ? VoiceReactionKind.LessonPhaseChanged : VoiceReactionKind.PartialUnderstanding,
            summary,
            0.72,
            phase == LessonVoicePhase.None ? LessonPhase : phase,
            null,
            isPartial: true,
            $"partial:{phase}:{normalized}");
    }

    public IReadOnlyList<VoiceReaction> ObserveFinal(string text)
    {
        _lastPartial = string.Empty;
        var normalized = Normalize(text);
        if (normalized.Length == 0) return [];

        var now = _timeProvider.GetUtcNow();
        Prune(now);
        if (IsRecent($"final:{normalized}", now)) return [];

        var reactions = new List<VoiceReaction>();
        var phase = _profile.Id.Equals("lesson", StringComparison.OrdinalIgnoreCase)
            ? DetectLessonPhase(normalized)
            : LessonVoicePhase.None;
        if (phase != LessonVoicePhase.None && phase != LessonPhase)
        {
            LessonPhase = phase;
            var phaseReaction = Publish(
                VoiceReactionKind.LessonPhaseChanged,
                $"Lesson phase · {DescribePhase(phase)}",
                0.96,
                phase,
                null,
                isPartial: false,
                $"phase:{phase}:{normalized}");
            if (phaseReaction is not null) reactions.Add(phaseReaction);
        }

        var action = DetectAction(normalized);
        if (action is not null)
        {
            var actionReaction = Publish(
                VoiceReactionKind.ActionSuggested,
                DescribeAction(action),
                action.Confidence,
                LessonPhase,
                action,
                isPartial: false,
                $"action:{action.Id}:{normalized}");
            if (actionReaction is not null) reactions.Add(actionReaction);
        }

        var finalReaction = Publish(
            VoiceReactionKind.FinalUnderstanding,
            LessonPhase == LessonVoicePhase.None
                ? "Heard and understood"
                : $"Tracking {DescribePhase(LessonPhase).ToLowerInvariant()}",
            0.98,
            LessonPhase,
            action,
            isPartial: false,
            $"final:{normalized}");
        if (finalReaction is not null) reactions.Add(finalReaction);
        return reactions;
    }

    public VoiceReaction? ObserveScreenContext(bool isSharing)
        => Publish(
            VoiceReactionKind.ScreenContextChanged,
            isSharing ? "Screen context is live" : "Screen context stopped",
            1,
            LessonPhase,
            null,
            isPartial: false,
            $"screen:{isSharing}");

    public VoiceReaction MarkActionExecuted(VoiceReactionAction action)
    {
        var executed = action with { WasAutoExecuted = true };
        return Publish(
            VoiceReactionKind.ActionExecuted,
            $"Opened {action.TargetSurface}",
            action.Confidence,
            LessonPhase,
            executed,
            isPartial: false,
            $"executed:{action.Id}:{_sequence + 1}",
            allowDuplicate: true)!;
    }

    public string BuildContextNote()
    {
        var parts = new List<string>();
        if (_profile.Id.Equals("lesson", StringComparison.OrdinalIgnoreCase) && LessonPhase != LessonVoicePhase.None)
            parts.Add($"Current lesson phase: {DescribePhase(LessonPhase)}.");
        if (Current?.Action is { } action)
            parts.Add(action.RequiresConfirmation
                ? $"A possible {action.Intent} action was detected but requires confirmation."
                : $"Live Voice detected a {action.Intent} intent.");
        return string.Join(' ', parts);
    }

    public void ResetTransientAfterTurn()
    {
        Current = null;
        _lastPartial = string.Empty;
        if (!_profile.ContinuousListening)
            LessonPhase = LessonVoicePhase.None;
    }

    private VoiceReactionAction? DetectAction(string normalized)
    {
        if (ContainsAny(normalized, "look this up", "look up ", "search for ", "find online", "browse for "))
            return new("voice.browse", HavenSurface.Browse, "browse", normalized, 0.97, RequiresConfirmation: false);
        if (ContainsAny(normalized, "whiteboard", "draw this", "sketch this", "put this on the board"))
            return new("voice.whiteboard", HavenSurface.Study, "whiteboard", normalized, 0.95, RequiresConfirmation: false);
        if (ContainsAny(normalized, "take notes", "write this down", "note this", "make a note"))
            return new("voice.notes", HavenSurface.Study, "notes", normalized, 0.95, RequiresConfirmation: false);
        if (ContainsAny(normalized, "remind me", "add this to my plan", "schedule this", "homework is due"))
            return new("voice.plan", HavenSurface.Plan, "plan", normalized, 0.94, RequiresConfirmation: true);
        return null;
    }

    private static LessonVoicePhase DetectLessonPhase(string normalized)
    {
        if (ContainsAny(normalized, "starter", "warm up", "warm-up", "do now", "bell work")) return LessonVoicePhase.Starter;
        if (ContainsAny(normalized, "homework", "for next lesson", "due next", "due tomorrow")) return LessonVoicePhase.Homework;
        if (ContainsAny(normalized, "review", "mark your", "check your answer", "model answer", "mark scheme")) return LessonVoicePhase.Review;
        if (ContainsAny(normalized, "quick check", "knowledge check", "question for you", "what is ", "can you tell me")) return LessonVoicePhase.KnowledgeCheck;
        if (ContainsAny(normalized, "try this", "have a go", "practice", "your turn")) return LessonVoicePhase.GuidedPractice;
        if (ContainsAny(normalized, "worksheet", "slide", "document", "resource", "look at the board")) return LessonVoicePhase.Resource;
        if (ContainsAny(normalized, "explain", "today we are learning", "today we're learning", "the key idea", "this means")) return LessonVoicePhase.Explanation;
        return LessonVoicePhase.None;
    }

    private VoiceReaction? Publish(
        VoiceReactionKind kind,
        string summary,
        double confidence,
        LessonVoicePhase phase,
        VoiceReactionAction? action,
        bool isPartial,
        string signature,
        bool allowDuplicate = false)
    {
        var now = _timeProvider.GetUtcNow();
        Prune(now);
        if (!allowDuplicate && IsRecent(signature, now)) return null;
        _recent[signature] = now;
        if (phase != LessonVoicePhase.None) LessonPhase = phase;
        return Current = new VoiceReaction(
            Interlocked.Increment(ref _sequence),
            kind,
            summary,
            Math.Clamp(confidence, 0, 1),
            now,
            LessonPhase,
            action,
            isPartial);
    }

    private bool IsRecent(string signature, DateTimeOffset now)
        => _recent.TryGetValue(signature, out var seen) && now - seen <= DuplicateWindow;

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _recent.Where(pair => now - pair.Value > DuplicateWindow).Select(pair => pair.Key).ToArray())
            _recent.Remove(key);
    }

    private static string Normalize(string text)
        => string.Join(' ', text.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.Ordinal));

    private static string DescribePhase(LessonVoicePhase phase) => phase switch
    {
        LessonVoicePhase.Starter => "Starter",
        LessonVoicePhase.Explanation => "Explanation",
        LessonVoicePhase.GuidedPractice => "Guided practice",
        LessonVoicePhase.KnowledgeCheck => "Knowledge check",
        LessonVoicePhase.Review => "Review",
        LessonVoicePhase.Homework => "Homework",
        LessonVoicePhase.Resource => "Resource",
        _ => "Conversation"
    };

    private static string DescribeAction(VoiceReactionAction action) => action.Intent switch
    {
        "browse" => "Browse request detected",
        "whiteboard" => "Whiteboard request detected",
        "notes" => "Notes request detected",
        "plan" => "Planning request needs confirmation",
        _ => "Voice action detected"
    };
}
