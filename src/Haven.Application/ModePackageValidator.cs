/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ModePackageValidator.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ModePackageValidator. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents mode package validator and keeps its related state and behavior together.
/// </summary>
public sealed class ModePackageValidator : IModePackageValidator
{
    /// <summary>
    /// Stores valid step kinds locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> ValidStepKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "chat", "tool", "browser", "planner", "filesystem", "command", "condition", "parallel"
    };

    /// <summary>
    /// Stores valid ui layouts locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> ValidUiLayouts = new(StringComparer.OrdinalIgnoreCase)
    {
        "chat", "dashboard", "wizard", "kanban", "list", "custom"
    };

    /// <summary>
    /// Stores reserved keys locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> ReservedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "chat", "teach", "do", "studio", "browse", "plan", "training", "home", "settings", "admin"
    };

    /// <summary>
    /// Validates this member before it crosses the next trust or persistence boundary.
    /// </summary>
    public ModePackageValidationResult Validate(DeclarativeModeDefinition definition)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var permissions = new List<string>();
        var gaps = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.Key))
            errors.Add("Mode key is required.");
        else if (definition.Key.Length < 3)
            errors.Add("Mode key must be at least 3 characters.");
        else if (!char.IsLetter(definition.Key[0]))
            errors.Add("Mode key must start with a letter.");
        else if (ReservedKeys.Contains(definition.Key))
            errors.Add($"'{definition.Key}' is a reserved key.");

        if (string.IsNullOrWhiteSpace(definition.Name))
            errors.Add("Mode name is required.");

        if (definition.Workflow.Steps.Length == 0)
            warnings.Add("Mode has no workflow steps.");

        foreach (var step in definition.Workflow.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id))
                errors.Add("Workflow step ID is required.");
            else if (definition.Workflow.Edges.ContainsKey(step.Id) &&
                     definition.Workflow.Edges[step.Id].Target is not null &&
                     !definition.Workflow.Steps.Any(s => s.Id == definition.Workflow.Edges[step.Id].Target))
                errors.Add($"Step '{step.Id}' references unknown target '{definition.Workflow.Edges[step.Id].Target}'.");

            if (!ValidStepKinds.Contains(step.Kind))
                warnings.Add($"Step '{step.Id}' has unknown kind '{step.Kind}'.");

            if (step.RequiredCapabilities is not null)
                gaps.AddRange(step.RequiredCapabilities);
        }

        if (!ValidUiLayouts.Contains(definition.Ui.Layout))
            warnings.Add($"Unknown UI layout '{definition.Ui.Layout}'.");

        if (definition.Permissions.FilePermission == "FullAccess")
            permissions.Add("Full file system access requested");
        if (definition.Permissions.CommandPermission == "FullAccess")
            permissions.Add("Full command execution access requested");
        if (definition.Permissions.AllowDesktopTools)
            permissions.Add("Desktop tools access requested");
        if (definition.Permissions.AllowFileSystemWrites)
            permissions.Add("File system write access requested");
        if (definition.Permissions.CustomCapabilities.Length > 0)
            permissions.AddRange(definition.Permissions.CustomCapabilities);

        if (definition.Capabilities.Length > 0)
        {
            foreach (var cap in definition.Capabilities)
                gaps.RemoveAll(g => g == cap);
        }

        return new ModePackageValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.Distinct().ToArray(),
            Warnings = warnings.Distinct().ToArray(),
            PermissionRequirements = permissions.Distinct().ToArray(),
            CapabilityGaps = gaps.Distinct().ToArray()
        };
    }
}
