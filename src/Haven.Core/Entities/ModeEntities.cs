namespace Haven.Core;

/// <summary>
/// Represents a mode definition.
/// </summary>
public sealed record ModeDefinition(
    Guid Id,
    string Key,
    string Name,
    string Description,
    string IconKey,
    HavenMode BaseMode,
    string SurfacesJson,
    string ToolAllowlistJson,
    string ToolDenylistJson,
    string CapabilitiesJson,
    string SystemPromptSuffix,
    ModeSource Source,
    ModeInstallState InstallState,
    string Author,
    string Version,
    string TagsJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsEnabled = true);

/// <summary>
/// Represents a mode version.
/// </summary>
public sealed record ModeVersion(
    Guid Id,
    Guid ModeId,
    int Major,
    int Minor,
    int Patch,
    string ManifestJson,
    string Changelog,
    DateTimeOffset PublishedAt);

/// <summary>
/// Represents a mode permission grant.
/// </summary>
public sealed record ModePermissionGrant(
    Guid Id,
    Guid ModeId,
    PermissionMode FilePermission,
    PermissionMode CommandPermission,
    PermissionMode BrowserPermission,
    bool AllowDesktopTools,
    bool AllowFileSystemWrites,
    DateTimeOffset GrantedAt);

/// <summary>
/// Represents a mode pin.
/// </summary>
public sealed record ModePin(
    Guid Id,
    Guid ModeId,
    int SortOrder,
    DateTimeOffset PinnedAt);

/// <summary>
/// Represents mode usage.
/// </summary>
public sealed record ModeUsage(
    Guid Id,
    Guid ModeId,
    DateOnly Date,
    int TurnCount,
    int CompletionCount,
    TimeSpan TotalDuration);
