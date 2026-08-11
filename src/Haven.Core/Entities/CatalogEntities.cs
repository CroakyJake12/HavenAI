namespace Haven.Core;

/// <summary>Represents an agent definition.</summary>
public sealed record AgentDefinition(
    Guid Id,
    string Name,
    string Description,
    string Instructions,
    string IconKey,
    string PreferredModel,
    string? FallbackModel,
    string DetectionRules,
    string PermissionsJson,
    bool IsBuiltIn,
    bool IsEnabled,
    DateTimeOffset UpdatedAt);

/// <summary>Represents a reusable prompt definition.</summary>
public sealed record PromptDefinition(
    Guid Id,
    string Name,
    string Description,
    string IconKey,
    string Instructions,
    bool Persists,
    bool IsBuiltIn,
    bool IsEnabled,
    DateTimeOffset UpdatedAt,
    bool IsAgentic = false,
    string AllowedModesJson = "[]");
