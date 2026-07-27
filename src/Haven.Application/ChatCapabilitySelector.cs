using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Selects the smallest capability set required for a turn. Explicit user selections
/// always win over automatic intent detection.
/// </summary>
public sealed class ChatCapabilitySelector
{
    public IReadOnlyList<OllamaToolDefinition> Select(
        string prompt,
        IReadOnlyList<OllamaToolDefinition> available,
        IReadOnlyCollection<string>? explicitlySelectedToolNames = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(available);

        if (explicitlySelectedToolNames is { Count: > 0 })
        {
            var selected = explicitlySelectedToolNames.ToHashSet(StringComparer.Ordinal);
            return available.Where(tool => selected.Contains(tool.Name)).ToArray();
        }

        var categories = DetectCategories(prompt);
        if (categories.Count == 0)
        {
            return [];
        }

        return available
            .Where(tool => categories.Any(category => MatchesCategory(tool.Name, category)))
            .ToArray();
    }

    public static bool IsGenericConversation(string prompt) =>
        DetectCategories(prompt).Count == 0;

    private static HashSet<CapabilityCategory> DetectCategories(string prompt)
    {
        var value = prompt.Trim().ToLowerInvariant();
        var result = new HashSet<CapabilityCategory>();

        if (ContainsAny(value, "search the web", "browse", "look online", "website", "web page", "url"))
        {
            result.Add(CapabilityCategory.Browser);
        }

        if (ContainsAny(value, "file", "folder", "project", "code", "build", "test", "compile", "nuget", "package"))
        {
            result.Add(CapabilityCategory.Workspace);
        }

        if (ContainsAny(value, "open app", "launch", "click", "type into", "press ", "window", "desktop"))
        {
            result.Add(CapabilityCategory.Computer);
        }

        if (ContainsAny(value, "remind me", "reminder", "schedule", "automation", "every day", "every week"))
        {
            result.Add(CapabilityCategory.Automation);
        }

        return result;
    }

    private static bool MatchesCategory(string toolName, CapabilityCategory category)
    {
        var prefix = category switch
        {
            CapabilityCategory.Browser => "browser_",
            CapabilityCategory.Workspace => "workspace_",
            CapabilityCategory.Computer => "computer_",
            CapabilityCategory.Automation => "automation_",
            _ => string.Empty
        };

        return toolName.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.Ordinal));

    private enum CapabilityCategory
    {
        Browser,
        Workspace,
        Computer,
        Automation
    }
}

/// <summary>
/// Chooses a temporary compatible model while keeping the user's selected model as
/// the model to restore after the capability turn.
/// </summary>
public sealed class CompatibleModelFallbackSelector
{
    public ModelFallbackSelection? Select(
        ModelDescriptor selected,
        IReadOnlyCollection<ModelDescriptor> installed,
        IReadOnlyCollection<ToolCapability> requiredCapabilities)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(requiredCapabilities);

        if (requiredCapabilities.All(selected.Supports))
        {
            return new ModelFallbackSelection(selected, selected, false);
        }

        var compatible = installed
            .Where(model => requiredCapabilities.All(model.Supports))
            .OrderBy(model => ModelDistance(selected, model))
            .ThenByDescending(model => model.ModifiedAt)
            .FirstOrDefault();

        return compatible is null
            ? null
            : new ModelFallbackSelection(compatible, selected, true);
    }

    private static double ModelDistance(ModelDescriptor selected, ModelDescriptor candidate)
    {
        var selectedSize = Math.Max(1L, selected.SizeBytes);
        var candidateSize = Math.Max(1L, candidate.SizeBytes);
        var sizeDistance = Math.Abs(Math.Log(candidateSize) - Math.Log(selectedSize));
        var familyPenalty = string.Equals(
            selected.Family,
            candidate.Family,
            StringComparison.OrdinalIgnoreCase)
            ? 0d
            : 0.5d;

        return sizeDistance + familyPenalty;
    }
}

public sealed record ModelFallbackSelection(
    ModelDescriptor ActiveModel,
    ModelDescriptor RestoreModel,
    bool IsFallback);
