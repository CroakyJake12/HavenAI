namespace Haven.Core;

[Flags]
public enum CapabilityPlatform
{
    None = 0,
    Windows = 1,
    Android = 2,
    All = Windows | Android
}

public enum CapabilityRiskClass
{
    ReadOnly = 0,
    Low = 1,
    Consequential = 2,
    Restricted = 3
}

public enum CapabilityAvailability
{
    Available = 0,
    PermissionRequired = 1,
    DependencyRequired = 2,
    Restricted = 3,
    Unsupported = 4
}

/// <summary>
/// Authoritative discovery and routing metadata for something Haven can do.
/// OwnerAppKey organises the catalogue; it never creates a separate tool loop.
/// </summary>
public sealed record CapabilityDefinition(
    Guid Id,
    string Key,
    string Name,
    string Description,
    string OwnerAppKey,
    string IconKey,
    string Instructions,
    string ImplementationKey,
    string SemanticActionsJson,
    CapabilityPlatform Platforms,
    CapabilityRiskClass RiskClass,
    CapabilityAvailability Availability,
    string DependenciesJson,
    string ProviderId,
    bool IsAttachable,
    bool IsAgentUsable,
    bool IsBuiltIn,
    bool IsEnabled,
    DateTimeOffset UpdatedAt);
