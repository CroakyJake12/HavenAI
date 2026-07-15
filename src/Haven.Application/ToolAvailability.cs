using Haven.Core;

namespace Haven.Application;

public enum ToolRuntimeKind
{
    Workspace,
    Computer,
    Browser,
    Automation
}

public sealed record ToolAvailabilityContext(
    HavenMode Mode,
    string? WorkspaceRoot,
    IReadOnlyCollection<ActivePlugin> Plugins,
    PermissionMode FilePermission,
    PermissionMode CommandPermission,
    PermissionMode BrowserPermission,
    bool IsWindowsHost,
    bool BrowserHostAvailable,
    bool BrowserInteractiveHostAvailable,
    bool AutomationHostAvailable)
{
    public bool HasExistingWorkspace =>
        !string.IsNullOrWhiteSpace(WorkspaceRoot) && Directory.Exists(WorkspaceRoot);

    public bool IsPluginActive(string name) =>
        Plugins.Any(plugin => plugin.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public sealed record ToolDefinitionSources(
    IReadOnlyList<OllamaToolDefinition> Workspace,
    IReadOnlyList<OllamaToolDefinition> Computer,
    IReadOnlyList<OllamaToolDefinition> BrowserBackground,
    IReadOnlyList<OllamaToolDefinition> BrowserInteractive,
    IReadOnlyList<OllamaToolDefinition> Automation,
    IReadOnlyList<OllamaToolDefinition> Macros);

public sealed class ToolAvailabilityPlan
{
    private static readonly HashSet<string> ContextualPluginNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Automate", "BrowserUse", "ComputerUse", "Macro", "Test", "WebSearch"
    };

    private readonly IReadOnlyDictionary<string, ToolRuntimeKind> _routes;
    private readonly IReadOnlyDictionary<string, string> _unavailableReasons;
    private readonly IReadOnlySet<string> _availableContextualPlugins;

    internal ToolAvailabilityPlan(
        IReadOnlyList<OllamaToolDefinition> definitions,
        IReadOnlyDictionary<string, ToolRuntimeKind> routes,
        IReadOnlyDictionary<string, string> unavailableReasons,
        IReadOnlySet<string> availableContextualPlugins)
    {
        Definitions = definitions;
        _routes = routes;
        _unavailableReasons = unavailableReasons;
        _availableContextualPlugins = availableContextualPlugins;
    }

    public IReadOnlyList<OllamaToolDefinition> Definitions { get; }

    public bool HasRuntime(ToolRuntimeKind runtime) => _routes.Values.Contains(runtime);

    public bool TryGetRuntime(string toolName, out ToolRuntimeKind runtime) =>
        _routes.TryGetValue(toolName, out runtime);

    public bool IsPluginAvailable(string pluginName) =>
        !ContextualPluginNames.Contains(pluginName) || _availableContextualPlugins.Contains(pluginName);

    public IReadOnlyCollection<ActivePlugin> FilterPlugins(IReadOnlyCollection<ActivePlugin> plugins) =>
        plugins.Where(plugin => IsPluginAvailable(plugin.Name)).ToArray();

    public string GetUnavailableReason(string toolName) =>
        _unavailableReasons.TryGetValue(toolName, out var reason)
            ? reason
            : $"Tool '{toolName}' is not registered for this Haven pass.";

    public ToolAvailabilityPlan RestrictToModel(ModelDescriptor model)
    {
        if (model.Supports(ToolCapability.Tools)) return this;

        var routes = new Dictionary<string, ToolRuntimeKind>(StringComparer.Ordinal);
        var definitions = new List<OllamaToolDefinition>();
        var reasons = new Dictionary<string, string>(_unavailableReasons, StringComparer.Ordinal);
        foreach (var definition in Definitions)
        {
            var runtime = _routes[definition.Name];
            var supported = runtime switch
            {
                ToolRuntimeKind.Computer => model.Supports(ToolCapability.ComputerUse),
                ToolRuntimeKind.Browser => model.Supports(ToolCapability.Browser),
                _ => false
            };
            if (supported)
            {
                definitions.Add(definition);
                routes.Add(definition.Name, runtime);
            }
            else
            {
                reasons[definition.Name] = $"Tool '{definition.Name}' is unavailable because model '{model.Name}' lacks the required tool capability.";
            }
        }

        var plugins = new HashSet<string>(_availableContextualPlugins, StringComparer.OrdinalIgnoreCase);
        if (!routes.Values.Contains(ToolRuntimeKind.Computer)) plugins.Remove("ComputerUse");
        if (!routes.Values.Contains(ToolRuntimeKind.Browser))
        {
            plugins.Remove("BrowserUse");
            plugins.Remove("WebSearch");
        }
        if (!routes.ContainsKey("run_tests")) plugins.Remove("Test");
        if (!routes.Values.Contains(ToolRuntimeKind.Automation))
        {
            plugins.Remove("Automate");
            plugins.Remove("Macro");
        }

        return new ToolAvailabilityPlan(definitions, routes, reasons, plugins);
    }
}

public sealed class ToolAvailabilityPlanner
{
    private static readonly HashSet<string> WorkspaceReadTools = new(StringComparer.Ordinal)
    {
        "list_files", "read_file", "search_files"
    };

    private static readonly HashSet<string> WorkspaceMutationTools = new(StringComparer.Ordinal)
    {
        "write_file", "replace_in_file"
    };

    public static ToolAvailabilityPlanner Default { get; } = new();

    public ToolAvailabilityPlan Create(ToolAvailabilityContext context, ToolDefinitionSources sources)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sources);

        var definitions = new List<OllamaToolDefinition>();
        var routes = new Dictionary<string, ToolRuntimeKind>(StringComparer.Ordinal);
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        var availablePlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        PlanWorkspace(context, sources.Workspace, definitions, routes, reasons, availablePlugins);
        PlanComputer(context, sources.Computer, definitions, routes, reasons, availablePlugins);
        PlanBrowser(context, sources.BrowserBackground, sources.BrowserInteractive, definitions, routes, reasons, availablePlugins);
        PlanAutomations(context, sources.Automation, sources.Macros, definitions, routes, reasons, availablePlugins);

        return new ToolAvailabilityPlan(definitions, routes, reasons, availablePlugins);
    }

    private static void PlanWorkspace(
        ToolAvailabilityContext context,
        IReadOnlyList<OllamaToolDefinition> source,
        List<OllamaToolDefinition> definitions,
        Dictionary<string, ToolRuntimeKind> routes,
        Dictionary<string, string> reasons,
        HashSet<string> availablePlugins)
    {
        var modeAllowed = context.Mode is HavenMode.Do or HavenMode.Studio;
        foreach (var definition in source)
        {
            if (!modeAllowed)
            {
                reasons[definition.Name] = $"Tool '{definition.Name}' is available only in Haven Do or Haven Studio.";
                continue;
            }
            if (!context.HasExistingWorkspace)
            {
                reasons[definition.Name] = $"Tool '{definition.Name}' requires a selected workspace folder that exists locally.";
                continue;
            }

            var allowed = WorkspaceReadTools.Contains(definition.Name) ||
                          WorkspaceMutationTools.Contains(definition.Name) && context.FilePermission is PermissionMode.AutoSafe or PermissionMode.FullAccess ||
                          definition.Name == "run_tests" && context.CommandPermission is PermissionMode.AutoSafe or PermissionMode.FullAccess ||
                          definition.Name == "run_command" && context.CommandPermission == PermissionMode.FullAccess;
            if (allowed)
            {
                Add(definition, ToolRuntimeKind.Workspace, definitions, routes);
                if (definition.Name == "run_tests") availablePlugins.Add("Test");
                continue;
            }

            reasons[definition.Name] = definition.Name switch
            {
                "write_file" or "replace_in_file" => $"Tool '{definition.Name}' requires Auto Safe or Full Access file permission.",
                "run_tests" => "Tool 'run_tests' requires Auto Safe or Full Access command permission.",
                "run_command" => "Tool 'run_command' requires Full Access command permission.",
                _ => $"Tool '{definition.Name}' has no explicit availability policy and was disabled."
            };
        }
    }

    private static void PlanComputer(
        ToolAvailabilityContext context,
        IReadOnlyList<OllamaToolDefinition> source,
        List<OllamaToolDefinition> definitions,
        Dictionary<string, ToolRuntimeKind> routes,
        Dictionary<string, string> reasons,
        HashSet<string> availablePlugins)
    {
        var explicitlyEnabled = context.IsPluginActive("ComputerUse");
        foreach (var definition in source)
        {
            if (!explicitlyEnabled)
            {
                reasons[definition.Name] = $"Tool '{definition.Name}' requires explicit @ComputerUse approval for this pass.";
                continue;
            }
            if (!context.IsWindowsHost)
            {
                reasons[definition.Name] = $"Tool '{definition.Name}' is unavailable because Computer Use currently requires Windows.";
                continue;
            }
            Add(definition, ToolRuntimeKind.Computer, definitions, routes);
        }
        if (explicitlyEnabled && context.IsWindowsHost && source.Count > 0) availablePlugins.Add("ComputerUse");
    }

    private static void PlanBrowser(
        ToolAvailabilityContext context,
        IReadOnlyList<OllamaToolDefinition> backgroundSource,
        IReadOnlyList<OllamaToolDefinition> interactiveSource,
        List<OllamaToolDefinition> definitions,
        Dictionary<string, ToolRuntimeKind> routes,
        Dictionary<string, string> reasons,
        HashSet<string> availablePlugins)
    {
        var browserUse = context.IsPluginActive("BrowserUse");
        var webSearch = context.IsPluginActive("WebSearch");
        var anyBrowserPlugin = browserUse || webSearch;
        var backgroundAllowed = (webSearch || browserUse && context.BrowserInteractiveHostAvailable) &&
                                context.BrowserHostAvailable && context.BrowserPermission != PermissionMode.Ask;
        var interactiveAllowed = browserUse && context.BrowserHostAvailable && context.BrowserInteractiveHostAvailable &&
                                 context.BrowserPermission == PermissionMode.FullAccess;

        foreach (var definition in backgroundSource)
        {
            if (backgroundAllowed)
                Add(definition, ToolRuntimeKind.Browser, definitions, routes);
            else
                reasons[definition.Name] = BrowserReason(definition.Name, anyBrowserPlugin, context);
        }
        foreach (var definition in interactiveSource)
        {
            if (interactiveAllowed)
                Add(definition, ToolRuntimeKind.Browser, definitions, routes);
            else if (!browserUse)
                reasons[definition.Name] = $"Tool '{definition.Name}' requires the interactive @BrowserUse plugin; @WebSearch is read-only.";
            else if (!context.BrowserHostAvailable)
                reasons[definition.Name] = $"Tool '{definition.Name}' is unavailable because no browser host is connected.";
            else if (!context.BrowserInteractiveHostAvailable)
                reasons[definition.Name] = $"Tool '{definition.Name}' requires the native Browse view to be open and attached.";
            else
                reasons[definition.Name] = $"Tool '{definition.Name}' requires Full Access browser permission or approval for this message.";
        }

        if (backgroundAllowed && browserUse && context.BrowserInteractiveHostAvailable) availablePlugins.Add("BrowserUse");
        if (backgroundAllowed && webSearch) availablePlugins.Add("WebSearch");
    }

    private static void PlanAutomations(
        ToolAvailabilityContext context,
        IReadOnlyList<OllamaToolDefinition> automationSource,
        IReadOnlyList<OllamaToolDefinition> macroSource,
        List<OllamaToolDefinition> definitions,
        Dictionary<string, ToolRuntimeKind> routes,
        Dictionary<string, string> reasons,
        HashSet<string> availablePlugins)
    {
        var modeAllowed = context.Mode is HavenMode.Do or HavenMode.Studio;
        var automate = context.IsPluginActive("Automate");
        var macro = context.IsPluginActive("Macro");
        PlanAutomationGroup("Automate", automate, modeAllowed, context.AutomationHostAvailable, automationSource,
            definitions, routes, reasons, availablePlugins);
        PlanAutomationGroup("Macro", macro, modeAllowed, context.AutomationHostAvailable, macroSource,
            definitions, routes, reasons, availablePlugins);
    }

    private static void PlanAutomationGroup(
        string pluginName,
        bool pluginActive,
        bool modeAllowed,
        bool hostAvailable,
        IReadOnlyList<OllamaToolDefinition> source,
        List<OllamaToolDefinition> definitions,
        Dictionary<string, ToolRuntimeKind> routes,
        Dictionary<string, string> reasons,
        HashSet<string> availablePlugins)
    {
        foreach (var definition in source)
        {
            if (!pluginActive)
                reasons[definition.Name] = $"Tool '{definition.Name}' requires @{pluginName}.";
            else if (!modeAllowed)
                reasons[definition.Name] = $"Tool '{definition.Name}' is available only in Haven Do or Haven Studio.";
            else if (!hostAvailable)
                reasons[definition.Name] = $"Tool '{definition.Name}' is unavailable because the local automation store is not connected.";
            else
                Add(definition, ToolRuntimeKind.Automation, definitions, routes);
        }
        if (pluginActive && modeAllowed && hostAvailable && source.Count > 0) availablePlugins.Add(pluginName);
    }

    private static string BrowserReason(string toolName, bool anyBrowserPlugin, ToolAvailabilityContext context)
    {
        if (!anyBrowserPlugin) return $"Tool '{toolName}' requires @WebSearch or @BrowserUse.";
        if (!context.BrowserHostAvailable) return $"Tool '{toolName}' is unavailable because no browser host is connected.";
        if (context.IsPluginActive("BrowserUse") && !context.IsPluginActive("WebSearch") && !context.BrowserInteractiveHostAvailable)
            return $"Tool '{toolName}' requires the native Browse view to be open when using @BrowserUse.";
        return $"Tool '{toolName}' requires Auto Safe or Full Access browser permission, or approval for this message.";
    }

    private static void Add(
        OllamaToolDefinition definition,
        ToolRuntimeKind runtime,
        List<OllamaToolDefinition> definitions,
        Dictionary<string, ToolRuntimeKind> routes)
    {
        if (!routes.TryAdd(definition.Name, runtime))
            throw new InvalidOperationException($"Tool definition '{definition.Name}' is registered by more than one runtime.");
        definitions.Add(definition);
    }
}
