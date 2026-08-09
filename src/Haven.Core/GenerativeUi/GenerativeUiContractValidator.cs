using System.Text.Json;

namespace Haven.Core;

/// <summary>Rejects arbitrary visual/code payloads and bounds the trusted HavenUI document contract.</summary>
public static class GenerativeUiContractValidator
{
    public const int CurrentContractVersion = 1;
    public const int MaximumDepth = 16;
    public const int MaximumComponents = 500;
    public const int MaximumJsonBytes = 512 * 1024;

    public static IReadOnlySet<string> TrustedComponentTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "HavenWorkspace", "HavenStack", "HavenGrid", "HavenSplitView", "HavenToolbar",
        "HavenText", "HavenMarkdown", "HavenButton", "HavenTextInput", "HavenSelect",
        "HavenToggle", "HavenSlider", "HavenProgress", "HavenCard", "HavenList",
        "HavenTable", "HavenTabs", "HavenForm", "HavenWizard", "HavenChart",
        "HavenGraph", "HavenCanvas", "HavenImage", "HavenStatus"
    };

    private static readonly HashSet<string> ForbiddenPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "style", "css", "xaml", "html", "javascript", "script", "code", "executable", "commandLine"
    };

    public static bool ValidatePropertyName(string name) => !ForbiddenPropertyNames.Contains(name);

    public static IReadOnlyList<string> Validate(GenUiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<string>();
        if (document.ContractVersion != CurrentContractVersion)
            errors.Add($"Contract version {document.ContractVersion} is unsupported.");
        if (document.DocumentId == Guid.Empty) errors.Add("Document ID must be stable and non-empty.");
        if (document.Origin.ThreadId == Guid.Empty) errors.Add("Thread ID must be non-empty.");
        if (document.Origin.InstanceId == Guid.Empty) errors.Add("Instance ID must be non-empty.");
        if (string.IsNullOrWhiteSpace(document.Origin.AppKey)) errors.Add("Owning App key is required.");
        if (JsonSerializer.SerializeToUtf8Bytes(document).Length > MaximumJsonBytes)
            errors.Add($"Document exceeds the {MaximumJsonBytes}-byte contract limit.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        Visit(document.Root, 1, ids, ref count, errors);
        if (count > MaximumComponents) errors.Add($"Document contains {count} components; maximum is {MaximumComponents}.");
        return errors;
    }

    public static void ValidateAndThrow(GenUiDocument document)
    {
        var errors = Validate(document);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
    }

    public static IReadOnlyList<string> Validate(GenUiEvent semanticEvent)
    {
        ArgumentNullException.ThrowIfNull(semanticEvent);
        var errors = new List<string>();
        if (semanticEvent.EventId == Guid.Empty) errors.Add("Event ID must be non-empty.");
        if (semanticEvent.Origin.InstanceId == Guid.Empty) errors.Add("Event instance ID must be non-empty.");
        if (string.IsNullOrWhiteSpace(semanticEvent.ComponentId)) errors.Add("Component ID is required.");
        if (string.IsNullOrWhiteSpace(semanticEvent.ActionId)) errors.Add("Action ID is required.");
        if (semanticEvent.StructuredPayload.ValueKind is JsonValueKind.Undefined)
            errors.Add("Structured payload must be explicit JSON.");
        if (JsonSerializer.SerializeToUtf8Bytes(semanticEvent).Length > MaximumJsonBytes)
            errors.Add($"Event exceeds the {MaximumJsonBytes}-byte contract limit.");
        return errors;
    }

    private static void Visit(
        GenUiComponent component,
        int depth,
        HashSet<string> ids,
        ref int count,
        List<string> errors)
    {
        count++;
        if (depth > MaximumDepth)
        {
            errors.Add($"Component tree exceeds maximum depth {MaximumDepth}.");
            return;
        }
        if (string.IsNullOrWhiteSpace(component.ComponentId)) errors.Add("Every component requires a stable ID.");
        else if (!ids.Add(component.ComponentId)) errors.Add($"Duplicate component ID '{component.ComponentId}'.");
        if (!TrustedComponentTypes.Contains(component.ComponentType))
            errors.Add($"Component '{component.ComponentType}' is not in the trusted HavenUI vocabulary.");
        foreach (var key in component.Properties.Keys)
            if (ForbiddenPropertyNames.Contains(key)) errors.Add($"Property '{key}' is forbidden in generated UI.");
        foreach (var action in component.Actions)
        {
            if (string.IsNullOrWhiteSpace(action.ActionId)) errors.Add($"Component '{component.ComponentId}' has an action without an ID.");
            if (string.IsNullOrWhiteSpace(action.TargetKey)) errors.Add($"Action '{action.ActionId}' has no route target.");
            if (action.Route == GenUiRouteKind.Local
                && (action.RequiresPermission || action.RiskClass >= CapabilityRiskClass.Consequential))
                errors.Add($"Local action '{action.ActionId}' cannot own consequential or permissioned work.");
            if (action.Route is GenUiRouteKind.Capability or GenUiRouteKind.External
                && action.RiskClass >= CapabilityRiskClass.Consequential
                && !action.RequiresPermission)
                errors.Add($"Consequential action '{action.ActionId}' must preserve the permission boundary.");
        }
        foreach (var child in component.Children) Visit(child, depth + 1, ids, ref count, errors);
    }
}
