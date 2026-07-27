/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/ProjectCreationService.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns ProjectCreationService, ProjectCreationKind, ProjectCreationRequest, ProjectCreationResult. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>
/// Represents project creation service and keeps its related state and behavior together.
/// </summary>
public sealed partial class ProjectCreationService(IWorkspaceToolService processes, IContainerRepository containers)
{
    /// <summary>
    /// Stores templates locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Console app"] = "console",
        ["Class library"] = "classlib",
        ["Web API"] = "webapi",
        ["Worker service"] = "worker"
    };

    /// <summary>
    /// Creates async with the invariants required by its callers.
    /// </summary>
    public async Task<ProjectCreationResult> CreateAsync(ProjectCreationRequest request, CancellationToken cancellationToken)
    {
        var name = ValidateName(request.Name);
        var parent = Path.GetFullPath(request.ParentFolder);
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException($"The destination folder does not exist: {parent}");
        var target = Path.GetFullPath(Path.Combine(parent, name));
        if (!IsDirectChild(target, parent)) throw new InvalidOperationException("The project must be created directly inside the selected destination.");
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            throw new IOException($"The destination already contains files: {target}");

        var createdDirectory = !Directory.Exists(target);
        if (createdDirectory) Directory.CreateDirectory(target);
        try
        {
            var template = request.Kind == ProjectCreationKind.NuGetPackage
                ? "classlib"
                : Templates.TryGetValue(request.TemplateName, out var value) ? value : "console";
            var result = await processes.RunProcessAsync(new ProcessRequest(
                "dotnet.exe", $"new {template} --name \"{name}\" --output \"{target}\"", parent, TimeSpan.FromMinutes(4)), cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new InvalidOperationException("dotnet new failed: " + Diagnostic(result));

            if (request.Kind == ProjectCreationKind.NuGetPackage)
            {
                ConfigurePackageProject(target, name, request.PackageDescription);
                var pack = await processes.RunProcessAsync(new ProcessRequest(
                    "dotnet.exe", "pack --configuration Release", target, TimeSpan.FromMinutes(8)), cancellationToken).ConfigureAwait(false);
                if (pack.ExitCode != 0)
                    throw new InvalidOperationException("The package project was created, but its first package build failed: " + Diagnostic(pack));
            }

            var definition = await RegisterAsync(name, target, cancellationToken).ConfigureAwait(false);
            return new ProjectCreationResult(definition, request.Kind == ProjectCreationKind.NuGetPackage
                ? $"Created {name} and built its first NuGet package in bin/Release."
                : $"Created {name} from the {request.TemplateName} template.");
        }
        catch
        {
            if (createdDirectory && Directory.Exists(target) && IsDirectChild(target, parent))
                Directory.Delete(target, recursive: true);
            throw;
        }
    }

    /// <summary>
    /// Performs connect asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<ProjectCreationResult> ConnectAsync(string path, CancellationToken cancellationToken)
    {
        var canonical = File.Exists(path) ? Path.GetDirectoryName(Path.GetFullPath(path))! : Path.GetFullPath(path);
        if (!Directory.Exists(canonical)) throw new DirectoryNotFoundException(canonical);
        var name = File.Exists(path)
            ? Path.GetFileNameWithoutExtension(path)
            : new DirectoryInfo(canonical).Name;
        var definition = await RegisterAsync(string.IsNullOrWhiteSpace(name) ? "Local project" : name, canonical, cancellationToken).ConfigureAwait(false);
        return new ProjectCreationResult(definition, $"Connected {definition.Name} to {canonical}.");
    }

    /// <summary>
    /// Performs register asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<ContainerDefinition> RegisterAsync(string name, string root, CancellationToken cancellationToken)
    {
        var existing = await containers.GetByModeAsync(HavenMode.Studio, cancellationToken).ConfigureAwait(false);
        var match = existing.FirstOrDefault(item => string.Equals(item.RootPath, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        if (match is not null) return match;
        var now = DateTimeOffset.UtcNow;
        var definition = new ContainerDefinition(Guid.NewGuid(), HavenMode.Studio, name, root, string.Empty, string.Empty, now, now);
        await containers.UpsertAsync(definition, cancellationToken).ConfigureAwait(false);
        return definition;
    }

    /// <summary>
    /// Performs the configure package project step owned by this component.
    /// </summary>
    private static void ConfigurePackageProject(string root, string name, string description)
    {
        var projectFile = Directory.EnumerateFiles(root, "*.csproj", SearchOption.TopDirectoryOnly).Single();
        var document = XDocument.Load(projectFile, LoadOptions.PreserveWhitespace);
        var project = document.Root ?? throw new InvalidDataException("The generated project file has no Project element.");
        var propertyGroup = new XElement("PropertyGroup",
            new XElement("PackageId", name),
            new XElement("Version", "0.1.0"),
            new XElement("Authors", Environment.UserName),
            new XElement("Description", string.IsNullOrWhiteSpace(description) ? $"{name} package" : description.Trim()),
            new XElement("GeneratePackageOnBuild", "true"),
            new XElement("PackageRequireLicenseAcceptance", "false"));
        project.Add(propertyGroup);
        document.Save(projectFile);
    }

    /// <summary>
    /// Validates name before it crosses the next trust or persistence boundary.
    /// </summary>
    private static string ValidateName(string value)
    {
        var name = value.Trim();
        if (!ProjectNamePattern().IsMatch(name) || name is "." or "..")
            throw new ArgumentException("Use 1-80 letters, numbers, dots, underscores, or hyphens for the project name.", nameof(value));
        return name;
    }

    /// <summary>
    /// Reports whether direct child applies to the current state.
    /// </summary>
    private static bool IsDirectChild(string target, string parent) =>
        string.Equals(Path.GetDirectoryName(target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Performs the diagnostic step owned by this component.
    /// </summary>
    private static string Diagnostic(ProcessResult result)
    {
        var value = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        value = value.Trim();
        return value.Length <= 1200 ? value : value[^1200..];
    }

    /// <summary>
    /// Performs the project name pattern step owned by this component.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,79}$")]
    private static partial Regex ProjectNamePattern();
}

/// <summary>
/// Lists the supported project creation kind values used to make state explicit and type-safe.
/// </summary>
public enum ProjectCreationKind { DotNetProject, NuGetPackage }

/// <summary>
/// Represents project creation request and keeps its related state and behavior together.
/// </summary>
public sealed record ProjectCreationRequest(ProjectCreationKind Kind, string Name, string ParentFolder, string TemplateName, string PackageDescription);
/// <summary>
/// Represents project creation result and keeps its related state and behavior together.
/// </summary>
public sealed record ProjectCreationResult(ContainerDefinition Project, string Message);
