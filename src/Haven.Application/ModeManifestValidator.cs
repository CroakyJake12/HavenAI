using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed class ModeManifestValidator
{
    private static readonly HashSet<string> AllowedSurfaces = new(StringComparer.OrdinalIgnoreCase)
        { "Chat", "Do", "Teach", "Studio", "Browse", "Plan", "Phone", "Dashboard", "Training" };

    private static readonly HashSet<string> AllowedCapabilities = new(StringComparer.OrdinalIgnoreCase)
        { "Text", "Vision", "Tools", "Browser", "ComputerUse", "WebSearch", "Embeddings" };

    private static readonly HashSet<string> ReservedKeys = new(StringComparer.OrdinalIgnoreCase)
        { "chat", "teach", "do", "studio", "browse", "plan", "training", "call", "home" };

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

public sealed record ModeManifestValidation(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
