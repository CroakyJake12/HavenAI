using Haven.Core;

namespace Haven.Application;

public sealed class CapabilityPreflightService
{
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

    private static bool Supports(ModelDescriptor model, ToolCapability capability) => capability switch
    {
        ToolCapability.Browser or ToolCapability.ComputerUse or ToolCapability.WebSearch => model.Supports(ToolCapability.Tools) || model.Supports(capability),
        _ => model.Supports(capability)
    };
}
