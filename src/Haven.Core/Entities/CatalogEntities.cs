namespace Haven.Core;

/// <summary>
/// Represents an agent definition.
/// </summary>
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

/// <summary>
/// Represents a plugin definition.
/// </summary>
public sealed record PluginDefinition(
    Guid Id,
    string Name,
    string Description,
    string IconKey,
    string Instructions,
    string CapabilitiesJson,
    string ConflictsJson,
    bool Persists,
    bool IsBuiltIn,
    bool IsEnabled,
    DateTimeOffset UpdatedAt,
    bool IsAgentic = false,
    string AllowedModesJson = "[]",
    string DashboardTilesJson = "[]");

/// <summary>
/// Represents a prompt definition.
/// </summary>
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
