/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ModeManifestValidator.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ModeManifestValidator, ModeManifestValidation. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents mode manifest validator and keeps its related state and behavior together.
/// </summary>
public sealed class ModeManifestValidator
{
    /// <summary>
    /// Stores allowed surfaces locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> AllowedSurfaces = new(StringComparer.OrdinalIgnoreCase)
        { "Chat", "Do", "Teach", "Studio", "Browse", "Plan", "Phone", "Dashboard", "Training" };

    /// <summary>
    /// Stores allowed capabilities locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> AllowedCapabilities = new(StringComparer.OrdinalIgnoreCase)
        { "Text", "Vision", "Tools", "Browser", "ComputerUse", "WebSearch", "Embeddings" };

    /// <summary>
    /// Stores reserved keys locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> ReservedKeys = new(StringComparer.OrdinalIgnoreCase)
        { "chat", "teach", "do", "studio", "browse", "plan", "training", "call", "home" };

    /// <summary>
    /// Validates this member before it crosses the next trust or persistence boundary.
    /// </summary>
    public ModeManifestValidation Validate(ModeDefinition mode, string manifestJson)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(mode.Key))
            errors.Add("Mode key is required.");
        else if (mode.Key.Length > 100)
            errors.Add("Mode key must be 100 characters or fewer.");
        else if (!char.IsLetter(mode.Key[0]))
            errors.Add("Mode key must start with a letter.");
        else if (!mode.Key.All(c => char.IsLetterOrDigit(c) || c == '-'))
            errors.Add("Mode key may only contain letters, digits, and hyphens.");

        if (string.IsNullOrWhiteSpace(mode.Name))
            errors.Add("Mode name is required.");
        else if (mode.Name.Length > 200)
            errors.Add("Mode name must be 200 characters or fewer.");

        if (mode.Description.Length > 2000)
            warnings.Add("Description is very long and may be truncated in the UI.");

        if (string.IsNullOrWhiteSpace(mode.SystemPromptSuffix))
            warnings.Add("Mode has no system prompt suffix. It will behave like the base mode.");

        if (!string.IsNullOrWhiteSpace(mode.SurfacesJson))
        {
            try
            {
                var surfaces = JsonSerializer.Deserialize<List<string>>(mode.SurfacesJson) ?? [];
                foreach (var surface in surfaces)
                {
                    if (!AllowedSurfaces.Contains(surface))
                        errors.Add($"Unknown surface '{surface}'. Allowed: {string.Join(", ", AllowedSurfaces)}");
                }
            }
            catch (JsonException)
            {
                errors.Add("Surfaces JSON is invalid.");
            }
        }

        if (!string.IsNullOrWhiteSpace(mode.PluginsJson))
        {
            try
            {
                var plugins = JsonSerializer.Deserialize<List<string>>(mode.PluginsJson) ?? [];
                foreach (var plugin in plugins)
                {
                    if (string.IsNullOrWhiteSpace(plugin))
                        errors.Add("Plugin name cannot be empty.");
                }
            }
            catch (JsonException)
            {
                errors.Add("Plugins JSON is invalid.");
            }
        }

        if (!string.IsNullOrWhiteSpace(manifestJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(manifestJson);
                if (doc.RootElement.TryGetProperty("requiredCapabilities", out var caps))
                {
                    foreach (var cap in caps.EnumerateArray())
                    {
                        var capStr = cap.GetString() ?? "";
                        if (!AllowedCapabilities.Contains(capStr))
                            errors.Add($"Unknown capability '{capStr}'.");
                    }
                }
                if (doc.RootElement.TryGetProperty("maxToolCalls", out var maxCalls))
                {
                    var val = maxCalls.GetInt32();
                    if (val < 1 || val > 100)
                        errors.Add("maxToolCalls must be between 1 and 100.");
                }
            }
            catch (JsonException)
            {
                errors.Add("Manifest JSON is invalid.");
            }
        }

        return new ModeManifestValidation(errors.Count == 0, errors, warnings);
    }
}

/// <summary>
/// Represents mode manifest validation and keeps its related state and behavior together.
/// </summary>
public sealed record ModeManifestValidation(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
