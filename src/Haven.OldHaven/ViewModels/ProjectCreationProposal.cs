using System.Security.Cryptography;
using System.Text;
using Haven.Desktop.Services;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// A reviewable, non-executing project creation plan. Commands are previews only until approved.
/// </summary>
public sealed record ProjectCreationProposal(
    ProjectCreationKind Kind,
    string ProjectName,
    string ParentFolder,
    string TargetFolder,
    string TemplateName,
    string Summary,
    IReadOnlyList<ProjectCreationFilePreview> Files,
    IReadOnlyList<ProjectCreationCommandPreview> Commands,
    string PackageDescription,
    string Fingerprint)
{
    public bool Matches(
        ProjectCreationKind kind,
        string prompt,
        string projectName,
        string parentFolder,
        string selectedTemplate,
        string packageDescription) =>
        string.Equals(
            Fingerprint,
            ProjectCreationProposalPlanner.CreateFingerprint(
                kind,
                prompt,
                projectName,
                parentFolder,
                selectedTemplate,
                packageDescription),
            StringComparison.Ordinal);
}

public sealed record ProjectCreationFilePreview(string RelativePath, string Purpose);

public sealed record ProjectCreationCommandPreview(
    string Executable,
    string Arguments,
    string WorkingDirectory)
{
    public string DisplayText => $"{Executable} {Arguments}";
}

/// <summary>
/// Converts creator inputs into a deterministic preview without creating files or starting processes.
/// </summary>
public static class ProjectCreationProposalPlanner
{
    private static readonly Dictionary<string, string> DotNetTemplates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Console app"] = "console",
            ["Class library"] = "classlib",
            ["Web API"] = "webapi",
            ["Worker service"] = "worker"
        };

    public static ProjectCreationProposal Build(
        ProjectCreationKind fallbackKind,
        string prompt,
        string projectName,
        string parentFolder,
        string selectedTemplate,
        string packageDescription)
    {
        var cleanName = ValidateProjectName(projectName);
        var cleanParent = ValidateParentFolder(parentFolder);
        var cleanPrompt = prompt?.Trim() ?? string.Empty;
        var cleanDescription = packageDescription?.Trim() ?? string.Empty;
        var (kind, templateName) = InferKindAndTemplate(
            fallbackKind,
            cleanPrompt,
            selectedTemplate);

        var targetFolder = Path.GetFullPath(Path.Combine(cleanParent, cleanName));
        if (!IsDirectChild(targetFolder, cleanParent))
        {
            throw new InvalidOperationException(
                "The project must be created directly inside the selected destination.");
        }

        var files = BuildFiles(kind, templateName, cleanName);
        var commands = BuildCommands(
            kind,
            templateName,
            cleanName,
            cleanParent,
            targetFolder);
        var summary = BuildSummary(kind, templateName, cleanName, cleanPrompt);

        return new ProjectCreationProposal(
            kind,
            cleanName,
            cleanParent,
            targetFolder,
            templateName,
            summary,
            files,
            commands,
            cleanDescription,
            CreateFingerprint(
                fallbackKind,
                cleanPrompt,
                cleanName,
                cleanParent,
                selectedTemplate,
                cleanDescription));
    }

    public static string CreateFingerprint(
        ProjectCreationKind fallbackKind,
        string prompt,
        string projectName,
        string parentFolder,
        string selectedTemplate,
        string packageDescription)
    {
        var payload = string.Join(
            "\n",
            ((int)fallbackKind).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            prompt?.Trim() ?? string.Empty,
            projectName?.Trim() ?? string.Empty,
            NormalizePathForFingerprint(parentFolder),
            selectedTemplate?.Trim() ?? string.Empty,
            packageDescription?.Trim() ?? string.Empty);

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static (ProjectCreationKind Kind, string TemplateName) InferKindAndTemplate(
        ProjectCreationKind fallbackKind,
        string prompt,
        string selectedTemplate)
    {
        var normalized = prompt.ToLowerInvariant();

        if (ContainsAny(
                normalized,
                "nuget",
                "package library",
                "publishable package"))
        {
            return (ProjectCreationKind.NuGetPackage, "Class library");
        }

        if (ContainsAny(normalized, "web api", "rest api", "http api", "web service"))
        {
            return (ProjectCreationKind.DotNetProject, "Web API");
        }

        if (ContainsAny(
                normalized,
                "worker",
                "background service",
                "scheduled service",
                "daemon"))
        {
            return (ProjectCreationKind.DotNetProject, "Worker service");
        }

        if (ContainsAny(
                normalized,
                "class library",
                "shared library",
                "reusable library",
                "sdk"))
        {
            return (ProjectCreationKind.DotNetProject, "Class library");
        }

        if (ContainsAny(
                normalized,
                "console",
                "command line",
                "command-line",
                "cli"))
        {
            return (ProjectCreationKind.DotNetProject, "Console app");
        }

        if (fallbackKind == ProjectCreationKind.NuGetPackage)
        {
            return (ProjectCreationKind.NuGetPackage, "Class library");
        }

        var templateName =
            !string.IsNullOrWhiteSpace(selectedTemplate) &&
            DotNetTemplates.ContainsKey(selectedTemplate)
                ? selectedTemplate
                : "Console app";

        return (ProjectCreationKind.DotNetProject, templateName);
    }

    private static List<ProjectCreationFilePreview> BuildFiles(
        ProjectCreationKind kind,
        string templateName,
        string projectName)
    {
        var projectFile = $"{projectName}.csproj";

        if (kind == ProjectCreationKind.NuGetPackage)
        {
            return
            [
                new(projectFile, "Package metadata and build configuration"),
                new("Class1.cs", "Initial public library type"),
                new(
                    $"bin/Release/{projectName}.0.1.0.nupkg",
                    "Package produced after the approved Release pack")
            ];
        }

        return templateName switch
        {
            "Web API" =>
            [
                new(projectFile, "Web SDK project configuration"),
                new("Program.cs", "HTTP application entry point"),
                new("appsettings.json", "Application settings"),
                new(
                    "Properties/launchSettings.json",
                    "Local launch profile")
            ],
            "Worker service" =>
            [
                new(projectFile, "Worker SDK project configuration"),
                new("Program.cs", "Host entry point"),
                new("Worker.cs", "Background service implementation"),
                new("appsettings.json", "Worker settings")
            ],
            "Class library" =>
            [
                new(projectFile, "Library project configuration"),
                new("Class1.cs", "Initial library type")
            ],
            _ =>
            [
                new(projectFile, "Console project configuration"),
                new("Program.cs", "Application entry point")
            ]
        };
    }

    private static List<ProjectCreationCommandPreview> BuildCommands(
        ProjectCreationKind kind,
        string templateName,
        string projectName,
        string parentFolder,
        string targetFolder)
    {
        var template = kind == ProjectCreationKind.NuGetPackage
            ? "classlib"
            : DotNetTemplates[templateName];

        var commands = new List<ProjectCreationCommandPreview>
        {
            new(
                "dotnet",
                $"new {template} --name {Quote(projectName)} --output {Quote(targetFolder)}",
                parentFolder)
        };

        if (kind == ProjectCreationKind.NuGetPackage)
        {
            commands.Add(
                new(
                    "dotnet",
                    "pack --configuration Release",
                    targetFolder));
        }

        return commands;
    }

    private static string BuildSummary(
        ProjectCreationKind kind,
        string templateName,
        string projectName,
        string prompt)
    {
        var kindLabel = kind == ProjectCreationKind.NuGetPackage
            ? "NuGet package"
            : templateName;

        return string.IsNullOrWhiteSpace(prompt)
            ? $"Create {projectName} as a local {kindLabel} project."
            : $"Create {projectName} as a local {kindLabel} project for: {prompt}";
    }

    private static string ValidateProjectName(string value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 80 ||
            name is "." or ".." ||
            name.Any(
                character =>
                    !(char.IsLetterOrDigit(character) ||
                      character is '_' or '.' or '-')))
        {
            throw new ArgumentException(
                "Use 1-80 letters, numbers, dots, underscores, or hyphens for the project name.",
                nameof(value));
        }

        return name;
    }

    private static string ValidateParentFolder(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Choose a destination folder before reviewing the proposal.",
                nameof(value));
        }

        var parent = Path.GetFullPath(value.Trim());
        if (!Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException(
                $"The destination folder does not exist: {parent}");
        }

        return parent;
    }

    private static bool IsDirectChild(string target, string parent) =>
        string.Equals(
            Path.GetDirectoryName(
                target.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)),
            parent.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static string NormalizePathForFingerprint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(value.Trim()).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return value.Trim();
        }
    }

    private static bool ContainsAny(
        string value,
        params string[] candidates) =>
        candidates.Any(
            candidate => value.Contains(candidate, StringComparison.Ordinal));

    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
