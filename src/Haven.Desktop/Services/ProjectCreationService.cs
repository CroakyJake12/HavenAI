using System.Text.RegularExpressions;
using System.Xml.Linq;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

public sealed partial class ProjectCreationService(IWorkspaceToolService processes, IContainerRepository containers)
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Console app"] = "console",
        ["Class library"] = "classlib",
        ["Web API"] = "webapi",
        ["Worker service"] = "worker"
    };

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

    private static string ValidateName(string value)
    {
        var name = value.Trim();
        if (!ProjectNamePattern().IsMatch(name) || name is "." or "..")
            throw new ArgumentException("Use 1-80 letters, numbers, dots, underscores, or hyphens for the project name.", nameof(value));
        return name;
    }

    private static bool IsDirectChild(string target, string parent) =>
        string.Equals(Path.GetDirectoryName(target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Diagnostic(ProcessResult result)
    {
        var value = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        value = value.Trim();
        return value.Length <= 1200 ? value : value[^1200..];
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,79}$")]
    private static partial Regex ProjectNamePattern();
}

public enum ProjectCreationKind { DotNetProject, NuGetPackage }

public sealed record ProjectCreationRequest(ProjectCreationKind Kind, string Name, string ParentFolder, string TemplateName, string PackageDescription);
public sealed record ProjectCreationResult(ContainerDefinition Project, string Message);
