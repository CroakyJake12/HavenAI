using System.Text.RegularExpressions;
using Haven.Core;

namespace Haven.Application;

public sealed record ExtensionManifestValidationResult(bool IsValid, IReadOnlyList<string> Errors);

/// <summary>Validates multi-package repository manifests before any executable content is installed.</summary>
public sealed partial class ExtensionManifestValidator
{
    public ExtensionManifestValidationResult Validate(ExtensionManifestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<string>();
        if (document.SchemaVersion != 1) errors.Add($"Unsupported manifest schema version {document.SchemaVersion}.");
        if (document.Packages.Count == 0) errors.Add("The repository manifest contains no packages.");
        if (document.Packages.Count > 100) errors.Add("A repository may expose at most 100 packages.");
        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in document.Packages)
        {
            ValidatePackage(package, errors);
            if (!packageIds.Add(package.PackageId)) errors.Add($"Duplicate package ID '{package.PackageId}'.");
        }
        return new ExtensionManifestValidationResult(errors.Count == 0, errors);
    }

    private static void ValidatePackage(ExtensionPackageManifest package, ICollection<string> errors)
    {
        var prefix = string.IsNullOrWhiteSpace(package.PackageId) ? "Package" : package.PackageId;
        if (!PackageIdPattern().IsMatch(package.PackageId ?? string.Empty)) errors.Add($"{prefix}: package ID is invalid.");
        if (!IsSafeRelativePath(package.PackagePath)) errors.Add($"{prefix}: package path must be a safe relative path.");
        if (string.IsNullOrWhiteSpace(package.DisplayName) || package.DisplayName.Length > 120) errors.Add($"{prefix}: display name is required and must be at most 120 characters.");
        if (!Version.TryParse(package.Version, out _)) errors.Add($"{prefix}: version must be a valid dotted numeric version.");
        if (string.IsNullOrWhiteSpace(package.HavenVersionRange)) errors.Add($"{prefix}: Haven compatibility range is required.");
        if (package.RequestedPermissions.HasFlag(ExtensionPermission.ProcessExecution) && package.Capabilities.Count == 0)
            errors.Add($"{prefix}: process execution cannot be requested without a declared capability.");
        if (package.PackageType == ExtensionPackageType.Skill && package.Capabilities.Count > 0)
            errors.Add($"{prefix}: a Skill-only package cannot declare executable capabilities.");
        if (package.PackageType == ExtensionPackageType.Plugin && package.Skills.Count > 0)
            errors.Add($"{prefix}: use PluginAndSkills when Skills are bundled.");
        ValidateUnique(package.Capabilities.Select(value => value.Id), $"{prefix}: capability", errors);
        ValidateUnique(package.Skills.Select(value => value.Id), $"{prefix}: Skill", errors);
        foreach (var capability in package.Capabilities)
        {
            if (!PackageIdPattern().IsMatch(capability.Id)) errors.Add($"{prefix}: capability ID '{capability.Id}' is invalid.");
            if (!IsSafeRelativePath(capability.EntryPoint)) errors.Add($"{prefix}: capability entry point must be a safe relative path.");
            if (!capability.RequiredPermissions.HasFlag(ExtensionPermission.ProcessExecution))
                errors.Add($"{prefix}: capability '{capability.Id}' must declare the process execution permission.");
            if ((capability.RequiredPermissions & ~package.RequestedPermissions) != 0)
                errors.Add($"{prefix}: capability '{capability.Id}' requests permissions not declared by the package.");
        }
        foreach (var skill in package.Skills)
            if (!IsSafeRelativePath(skill.InstructionPath)) errors.Add($"{prefix}: Skill instruction path must be a safe relative path.");
    }

    private static bool IsSafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Split('/', '\\').Any(part => part == "..");

    private static void ValidateUnique(IEnumerable<string> values, string label, ICollection<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
            if (!seen.Add(value)) errors.Add($"{label} ID '{value}' is duplicated.");
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9.-]{1,126}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();
}
