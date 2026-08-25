// Model governance: personality personalisation, nicknames and per-model permission policy.

namespace Haven.Core;

/// <summary>Five-point human-readable strength scale shared by all personality controls.</summary>
public enum PersonalityLevel { VeryLow = 0, Low = 1, Moderate = 2, High = 3, VeryHigh = 4 }

/// <summary>Shared Haven personality controls; every value is user-understandable, never a raw number.</summary>
public sealed record ModelPersonality(
    PersonalityLevel Friendliness = PersonalityLevel.Moderate,
    PersonalityLevel MemoryReferences = PersonalityLevel.Moderate,
    PersonalityLevel Seriousness = PersonalityLevel.Moderate,
    PersonalityLevel Verbosity = PersonalityLevel.Moderate,
    PersonalityLevel Initiative = PersonalityLevel.Moderate,
    PersonalityLevel ExplanationDepth = PersonalityLevel.Moderate)
{
    public static readonly ModelPersonality Defaults = new();
}

/// <summary>Per-model personalisation entry. Null members mean "use Haven defaults" (inheritance).</summary>
public sealed record ModelPersonalisationEntry(
    string ModelKey,
    string? Nickname = null,
    ModelPersonality? Personality = null);

/// <summary>Capabilities that a model permission rule can deny for matched models.</summary>
public enum RestrictedModelCapability
{
    EditFiles = 0,
    RunCommands = 1,
    ComputerUse = 2,
    BrowserAutomation = 3
}

/// <summary>What a permission rule matches against.</summary>
public enum ModelPermissionTargetKind
{
    ExactModel = 0,
    ModelFamily = 1,
    Provider = 2,
    ParameterSizeBelow = 3,
    LocalModels = 4,
    CloudModels = 5
}

/// <summary>Where a permission rule applies.</summary>
public enum ModelPermissionScope { ThisDevice = 0, AcrossMesh = 1 }

/// <summary>
/// One deny-rule restricting capabilities for matched models. Parameter-size rules never apply when
/// parameter count is unknown, forcing a more specific rule instead of guessing.
/// </summary>
public sealed record ModelPermissionRule(
    Guid Id,
    ModelPermissionTargetKind Target,
    string Match,
    double? MaxParameterBillion,
    ModelPermissionScope Scope,
    HashSet<RestrictedModelCapability> Denied)
{
    public static ModelPermissionRule Create(
        ModelPermissionTargetKind target,
        string match,
        ModelPermissionScope scope,
        params RestrictedModelCapability[] denied)
        => new(Guid.NewGuid(), target, match ?? string.Empty, null, scope, denied.ToHashSet());
}

/// <summary>Persisted model permission policy. Absence of a matching deny rule means allowed.</summary>
public sealed record ModelPermissionPolicy(IReadOnlyList<ModelPermissionRule> Rules)
{
    public static readonly ModelPermissionPolicy Empty = new([]);
}

/// <summary>Result of evaluating a model against one restricted capability.</summary>
public sealed record ModelPermissionDecision(bool Allowed, ModelPermissionRule? DenyingRule, string? Reason)
{
    public static readonly ModelPermissionDecision Allow = new(true, null, null);
    public static ModelPermissionDecision Denied(ModelPermissionRule rule, string reason) => new(false, rule, reason);
}
