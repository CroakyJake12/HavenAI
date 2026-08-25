/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/Updates/WindowsInstallationDetector.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns InstallationInfo and WindowsInstallationDetector. Read the member comments below as a map of each responsibility.
 * How: Detection uses only portable signals (process base directory and environment variables) so this assembly keeps compiling cross-platform; no WinRT, WMI or P/Invoke is involved.
 * Why: Update policy must know whether the Microsoft Store owns updates without dragging Windows-specific dependencies into shared code.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file. Keep detection honest: never guess Store management that signals do not prove.
 */

using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// The result of installation-source detection.
/// </summary>
/// <param name="Source">Best supported classification from available signals.</param>
/// <param name="PackageFamilyName">Packaged identity (package family name) when one was detected, otherwise <c>null</c>.</param>
public sealed record InstallationInfo(InstallationSource Source, string? PackageFamilyName);

/// <summary>
/// Detects how the running Haven instance was installed using portable, dependency-free signals.
/// </summary>
/// <remarks>
/// Detection limitations, stated honestly:
/// <list type="bullet">
/// <item>A process base directory containing <c>\WindowsApps\</c> is the reliable signature of a Microsoft Store (MSIX) deployment, including installs on secondary drives.</item>
/// <item>Packaged processes on Windows 10 1809+ expose a <c>PACKAGE_FAMILY_NAME</c> environment variable. Its presence proves packaged identity but NOT Store management (sideloaded/enterprise MSIX also has it), so those installs report <see cref="InstallationSource.Unknown"/> rather than pretending to be Store-managed.</item>
/// <item>Absence of both signals is treated as a direct install; exotic deployment hosts could still misclassify, and callers must surface the source honestly instead of asserting certainty.</item>
/// </list>
/// </remarks>
public static class WindowsInstallationDetector
{
    /// <summary>
    /// Performs detect installation source for the current process.
    /// </summary>
    /// <returns>An <see cref="InstallationInfo"/> describing the best-supported classification.</returns>
    public static InstallationInfo DetectInstallationSource()
    {
        var packageFamilyName = Environment.GetEnvironmentVariable("PACKAGE_FAMILY_NAME");
        var trimmedFamilyName = string.IsNullOrWhiteSpace(packageFamilyName) ? null : packageFamilyName.Trim();
        var baseDirectory = AppContext.BaseDirectory;

        if (baseDirectory.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase))
        {
            return new InstallationInfo(InstallationSource.MicrosoftStore, trimmedFamilyName);
        }

        if (trimmedFamilyName is not null)
        {
            return new InstallationInfo(InstallationSource.Unknown, trimmedFamilyName);
        }

        return new InstallationInfo(InstallationSource.DirectInstall, null);
    }
}
