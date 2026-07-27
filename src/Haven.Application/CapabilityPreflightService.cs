/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/CapabilityPreflightService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns CapabilityPreflightService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents capability preflight service and keeps its related state and behavior together.
/// </summary>
public sealed class CapabilityPreflightService
{
    /// <summary>
    /// Performs the evaluate step owned by this component.
    /// </summary>
    public CapabilityPreflightResult Evaluate(
        ModelDescriptor activeModel,
        IReadOnlyCollection<ActivePlugin> plugins,
        bool hasImageAttachments,
        IReadOnlyCollection<ModelDescriptor> installedModels)
    {
        var requirements = new List<CapabilityRequirement> { new(ToolCapability.Text, "Chat requires text generation.") };
        if (hasImageAttachments) requirements.Add(new(ToolCapability.Vision, "An image is attached."));

        foreach (var plugin in plugins)
        {
            switch (plugin.Name)
            {
                case "BrowserUse": requirements.Add(new(ToolCapability.Browser, "@BrowserUse is active.")); break;
                case "ComputerUse": requirements.Add(new(ToolCapability.ComputerUse, "@ComputerUse is active.")); break;
                case "WebSearch": requirements.Add(new(ToolCapability.WebSearch, "@WebSearch is active.")); break;
                case "Automate": requirements.Add(new(ToolCapability.Tools, "@Automate creates Scheduled Actions through a tool call.")); break;
                case "Macro": requirements.Add(new(ToolCapability.Tools, "@Macro creates or inspects macros through a tool call.")); break;
                case "Test": requirements.Add(new(ToolCapability.Tools, "@Test runs targeted workspace tests through a tool call.")); break;
            }
        }

        requirements = requirements.DistinctBy(x => x.Capability).ToList();
        var missing = requirements.Where(r => !Supports(activeModel, r.Capability)).ToList();
        if (missing.Count == 0) return CapabilityPreflightResult.Compatible(requirements);

        var suggested = installedModels.FirstOrDefault(model => missing.All(m => Supports(model, m.Capability)));
        return new(false, requirements, missing, suggested);
    }

    /// <summary>
    /// Performs the supports step owned by this component.
    /// </summary>
    private static bool Supports(ModelDescriptor model, ToolCapability capability) => capability switch
    {
        ToolCapability.Browser or ToolCapability.ComputerUse or ToolCapability.WebSearch => model.Supports(ToolCapability.Tools) || model.Supports(capability),
        _ => model.Supports(capability)
    };
}
