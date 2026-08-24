using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>Presentation metadata for the capability picker used by the remaining Studio chat surface.</summary>
public sealed record CapabilityPickerDefinition(
    Guid Id,
    string Name,
    string Description,
    string IconKey,
    string Instructions,
    bool Persists,
    bool IsAgentic,
    string AllowedModesJson,
    string ConflictsJson)
{
    public static CapabilityPickerDefinition FromDefinition(CapabilityDefinition capability) => new(
        capability.Id,
        capability.Name,
        capability.Description,
        capability.IconKey,
        capability.Instructions,
        Persists: false,
        IsAgentic: capability.IsAgentUsable,
        AllowedModesJson: "[]",
        ConflictsJson: "[]");
}
