namespace Haven.Core;

public enum ExtensionPackageType { Plugin = 0, Skill = 1, PluginAndSkills = 2 }
public enum ExtensionSourceType { GitHubRepository = 0, LocalRepository = 1 }
public enum ExtensionUpdateMode { Manual = 0, Notify = 1, Automatic = 2 }
public enum ExtensionInstallState { Available = 0, Installing = 1, Installed = 2, UpdateAvailable = 3, Disabled = 4, Failed = 5, Incompatible = 6, Deprecated = 7 }

[Flags]
public enum ExtensionPermission
{
    None = 0,
    ReadHavenData = 1 << 0,
    ModifyHavenData = 1 << 1,
    ProjectRead = 1 << 2,
    ProjectWrite = 1 << 3,
    FileSystemRead = 1 << 4,
    FileSystemWrite = 1 << 5,
    ProcessExecution = 1 << 6,
    NetworkAccess = 1 << 7,
    ConnectorAccess = 1 << 8,
    HavenApiAccess = 1 << 9
}

public sealed record ExtensionSource(
    Guid Id,
    ExtensionSourceType Type,
    string DisplayName,
    string RepositoryUri,
    string? Branch,
    bool IsPrivate,
    string? ConnectedAccountId,
    ExtensionUpdateMode UpdateMode,
    bool IsEnabled,
    DateTimeOffset? LastRefreshedAt,
    string? SafeLastError);

public sealed record ExtensionCapabilityManifest(
    string Id,
    string DisplayName,
    string Description,
    string EntryPoint,
    IReadOnlyList<string> SemanticActions,
    ExtensionPermission RequiredPermissions);

public sealed record ExtensionSkillManifest(
    string Id,
    string DisplayName,
    string Description,
    string InstructionPath,
    bool EnabledByDefault);

public sealed record ExtensionPackageManifest(
    string PackageId,
    string PackagePath,
    string DisplayName,
    ExtensionPackageType PackageType,
    string Version,
    string HavenVersionRange,
    string Description,
    string Author,
    string Publisher,
    string? Homepage,
    string? License,
    ExtensionPermission RequestedPermissions,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<ExtensionCapabilityManifest> Capabilities,
    IReadOnlyList<ExtensionSkillManifest> Skills,
    string? UpdateManifestPath,
    bool Deprecated = false);

public sealed record InstalledExtensionPackage(
    Guid Id,
    Guid SourceId,
    ExtensionPackageManifest Manifest,
    string InstallPath,
    ExtensionPermission GrantedPermissions,
    ExtensionInstallState State,
    bool IsEnabled,
    bool HasLocalModifications,
    string ContentHash,
    DateTimeOffset InstalledAt,
    DateTimeOffset UpdatedAt,
    string? AvailableVersion = null,
    string? SafeLastError = null);

public sealed record ExtensionManifestDocument(int SchemaVersion, IReadOnlyList<ExtensionPackageManifest> Packages);

public sealed record DiscoveredExtensionPackage(
    Guid SourceId,
    ExtensionPackageManifest Manifest,
    string MaterializedRepositoryPath,
    string ContentHash,
    ExtensionInstallState State,
    string? SafeError = null);
