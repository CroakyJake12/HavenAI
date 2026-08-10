using Haven.Core;

namespace Haven.Application;

public sealed record GenUiQualityIssue(string ComponentId, string Message);

/// <summary>
/// Deterministic pre-presentation quality checks for generated documents.
/// This complements contract validation by checking whether the declaration
/// is plausibly useful for interaction rather than merely syntactically valid.
/// </summary>
public static class GenUiDocumentQualityValidator
{
    private static readonly HashSet<string> Containers = new(StringComparer.OrdinalIgnoreCase)
    {
        "HavenWorkspace", "HavenStack", "HavenForm", "HavenWizard", "HavenToolbar",
        "HavenGrid", "HavenSplitView", "HavenCard", "HavenTabs"
    };

    private static readonly HashSet<string> Interactive = new(StringComparer.OrdinalIgnoreCase)
    {
        "HavenButton", "HavenTextInput", "HavenSelect", "HavenToggle", "HavenSlider"
    };

    public static IReadOnlyList<GenUiQualityIssue> Validate(GenUiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = new List<GenUiQualityIssue>();
        Visit(document.Root, document.Origin.TemplateId is null, issues);
        return issues;
    }

    private static void Visit(GenUiComponent component, bool customDocument, List<GenUiQualityIssue> issues)
    {
        if (Containers.Contains(component.ComponentType) && component.Children.Count == 0)
            issues.Add(new(component.ComponentId, $"Container '{component.ComponentId}' has no children."));

        if (customDocument && Interactive.Contains(component.ComponentType) && component.Actions.Count == 0)
            issues.Add(new(component.ComponentId, $"Interactive component '{component.ComponentId}' has no action binding."));

        foreach (var child in component.Children) Visit(child, customDocument, issues);
    }
}
