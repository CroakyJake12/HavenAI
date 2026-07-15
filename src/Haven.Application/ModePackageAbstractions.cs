using Haven.Core;

namespace Haven.Application;

public interface IModePackageValidator
{
    ModePackageValidationResult Validate(DeclarativeModeDefinition definition);
}

public interface IModePackageInstaller
{
    Task<ModePackageInstallResult> InstallAsync(ModePackageManifest manifest, CancellationToken cancellationToken);
    Task<ModePackageInstallResult> UpdateAsync(Guid modeId, ModePackageManifest manifest, CancellationToken cancellationToken);
    Task<bool> RollbackAsync(Guid modeId, CancellationToken cancellationToken);
    Task<bool> UninstallAsync(Guid modeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InstalledModeInfo>> GetInstalledModesAsync(CancellationToken cancellationToken);
}

public sealed class InstalledModeInfo
{
    public Guid ModeId { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public ModeSource Source { get; init; }
    public DateTimeOffset InstalledAt { get; init; }
    public bool IsEnabled { get; init; }
}
