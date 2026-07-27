/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ToolAvailability.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ToolRuntimeKind, ToolAvailabilityContext, ToolDefinitionSources, ToolAvailabilityPlan, ToolAvailabilityPlanner. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Lists the supported tool runtime kind values used to make state explicit and type-safe.
/// </summary>
public enum ToolRuntimeKind { Workspace, Computer, Browser, Automation }

/// <summary>
/// Represents tool availability context and keeps its related state and behavior together.
/// </summary>
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
    /// <summary>
    /// Reports whether existing workspace applies to the current state.
    /// </summary>
    public bool HasExistingWorkspace => !string.IsNullOrWhiteSpace(WorkspaceRoot) && Directory.Exists(WorkspaceRoot);
    /// <summary>
    /// Reports whether plugin active applies to the current state.
    /// </summary>
    public bool IsPluginActive(string name) => Plugins.Any(plugin => plugin.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Represents tool definition sources and keeps its related state and behavior together.
/// </summary>
public sealed record ToolDefinitionSources(
    IReadOnlyList<OllamaToolDefinition> Workspace,
    IReadOnlyList<OllamaToolDefinition> Computer,
    IReadOnlyList<OllamaToolDefinition> BrowserBackground,
    IReadOnlyList<OllamaToolDefinition> BrowserInteractive,
    IReadOnlyList<OllamaToolDefinition> Automation,
    IReadOnlyList<OllamaToolDefinition> Macros);

/// <summary>
/// Represents tool availability plan and keeps its related state and behavior together.
/// </summary>
public sealed class ToolAvailabilityPlan
{
    /// <summary>
    /// Stores contextual plugin names locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> ContextualPluginNames = new(StringComparer.OrdinalIgnoreCase)
    { "Automate", "BrowserUse", "ComputerUse", "Macro", "Test", "WebSearch" };
    /// <summary>
    /// Stores routes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IReadOnlyDictionary<string, ToolRuntimeKind> _routes;
    /// <summary>
    /// Stores unavailable reasons locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IReadOnlyDictionary<string, string> _unavailableReasons;
    /// <summary>
    /// Stores available contextual plugins locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IReadOnlySet<string> _availableContextualPlugins;

    internal ToolAvailabilityPlan(IReadOnlyList<OllamaToolDefinition> definitions,
        IReadOnlyDictionary<string, ToolRuntimeKind> routes,
        IReadOnlyDictionary<string, string> unavailableReasons,
        IReadOnlySet<string> availableContextualPlugins)
    {
        Definitions = definitions;
        _routes = routes;
        _unavailableReasons = unavailableReasons;
        _availableContextualPlugins = availableContextualPlugins;
    }

    /// <summary>
    /// Gets or updates definitions, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<OllamaToolDefinition> Definitions { get; }
    /// <summary>
    /// Reports whether runtime applies to the current state.
    /// </summary>
    public bool HasRuntime(ToolRuntimeKind runtime) => _routes.Values.Contains(runtime);
    /// <summary>
    /// Attempts to get runtime and reports the result without using failure for normal control flow.
    /// </summary>
    public bool TryGetRuntime(string toolName, out ToolRuntimeKind runtime) => _routes.TryGetValue(toolName, out runtime);
    /// <summary>
    /// Reports whether plugin available applies to the current state.
    /// </summary>
    public bool IsPluginAvailable(string pluginName) => !ContextualPluginNames.Contains(pluginName) || _availableContextualPlugins.Contains(pluginName);
    /// <summary>
    /// Performs the filter plugins step owned by this component.
    /// </summary>
    public IReadOnlyCollection<ActivePlugin> FilterPlugins(IReadOnlyCollection<ActivePlugin> plugins) => plugins.Where(plugin => IsPluginAvailable(plugin.Name)).ToArray();
    /// <summary>
    /// Retrieves unavailable reason for the current operation.
    /// </summary>
    public string GetUnavailableReason(string toolName) => _unavailableReasons.TryGetValue(toolName, out var reason) ? reason : $"Tool '{toolName}' is not registered for this Haven pass.";

    /// <summary>
    /// Performs the restrict to model step owned by this component.
    /// </summary>
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
            else reasons[definition.Name] = $"Tool '{definition.Name}' is unavailable because model '{model.Name}' lacks the required tool capability.";
        }
        var plugins = new HashSet<string>(_availableContextualPlugins, StringComparer.OrdinalIgnoreCase);
        if (!routes.Values.Contains(ToolRuntimeKind.Computer)) plugins.Remove("ComputerUse");
        if (!routes.Values.Contains(ToolRuntimeKind.Browser)) { plugins.Remove("BrowserUse"); plugins.Remove("WebSearch"); }
        if (!routes.ContainsKey("run_tests")) plugins.Remove("Test");
        if (!routes.Values.Contains(ToolRuntimeKind.Automation)) { plugins.Remove("Automate"); plugins.Remove("Macro"); }
        return new ToolAvailabilityPlan(definitions, routes, reasons, plugins);
    }
}

/// <summary>
/// Represents tool availability planner and keeps its related state and behavior together.
/// </summary>
public sealed class ToolAvailabilityPlanner
{
    /// <summary>
    /// Stores workspace read tools locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> WorkspaceReadTools = new(StringComparer.Ordinal)
    { "list_files", "read_file", "search_files", "preview_change_set" };
    /// <summary>
    /// Stores workspace mutation tools locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> WorkspaceMutationTools = new(StringComparer.Ordinal)
    { "write_file", "replace_in_file", "apply_change_set" };
    /// <summary>
    /// Gets or updates default, the bindable or domain state represented by this property.
    /// </summary>
    public static ToolAvailabilityPlanner Default { get; } = new();

    /// <summary>
    /// Creates this member with the invariants required by its callers.
    /// </summary>
    public ToolAvailabilityPlan Create(ToolAvailabilityContext context, ToolDefinitionSources sources)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sources);
        var definitions = new List<OllamaToolDefinition>();
        var routes = new Dictionary<string, ToolRuntimeKind>(StringComparer.Ordinal);
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        var plugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PlanWorkspace(context, sources.Workspace, definitions, routes, reasons, plugins);
        PlanComputer(context, sources.Computer, definitions, routes, reasons, plugins);
        PlanBrowser(context, sources.BrowserBackground, sources.BrowserInteractive, definitions, routes, reasons, plugins);
        PlanAutomations(context, sources.Automation, sources.Macros, definitions, routes, reasons, plugins);
        return new ToolAvailabilityPlan(definitions, routes, reasons, plugins);
    }

    /// <summary>
    /// Performs the plan workspace step owned by this component.
    /// </summary>
    private static void PlanWorkspace(ToolAvailabilityContext context, IReadOnlyList<OllamaToolDefinition> source,
        List<OllamaToolDefinition> definitions, Dictionary<string, ToolRuntimeKind> routes,
        Dictionary<string, string> reasons, HashSet<string> plugins)
    {
        var modeAllowed = context.Mode is HavenMode.Do or HavenMode.Studio;
        foreach (var definition in source)
        {
            if (!modeAllowed) { reasons[definition.Name] = $"Tool '{definition.Name}' is available only in Haven Do or Haven Studio."; continue; }
            if (!context.HasExistingWorkspace) { reasons[definition.Name] = $"Tool '{definition.Name}' requires a selected workspace folder that exists locally."; continue; }
            if (RuntimeSafetyState.IsSafeMode && !WorkspaceReadTools.Contains(definition.Name))
            {
                reasons[definition.Name] = SafeModeReason(definition.Name);
                continue;
            }
            var allowed = WorkspaceReadTools.Contains(definition.Name)
                || WorkspaceMutationTools.Contains(definition.Name) && context.FilePermission is PermissionMode.AutoSafe or PermissionMode.FullAccess
                || definition.Name == "run_tests" && context.CommandPermission is PermissionMode.AutoSafe or PermissionMode.FullAccess
                || definition.Name == "run_command" && context.CommandPermission == PermissionMode.FullAccess;
            if (allowed)
            {
                Add(definition, ToolRuntimeKind.Workspace, definitions, routes);
                if (definition.Name == "run_tests") plugins.Add("Test");
            }
            else reasons[definition.Name] = definition.Name switch
            {
                "write_file" or "replace_in_file" or "apply_change_set" => $"Tool '{definition.Name}' requires Auto Safe or Full Access file permission.",
                "run_tests" => "Tool 'run_tests' requires Auto Safe or Full Access command permission.",
                "run_command" => "Tool 'run_command' requires Full Access command permission.",
                _ => $"Tool '{definition.Name}' has no explicit availability policy and was disabled."
            };
        }
    }

    /// <summary>
    /// Performs the plan computer step owned by this component.
    /// </summary>
    private static void PlanComputer(ToolAvailabilityContext context, IReadOnlyList<OllamaToolDefinition> source,
        List<OllamaToolDefinition> definitions, Dictionary<string, ToolRuntimeKind> routes,
        Dictionary<string, string> reasons, HashSet<string> plugins)
    {
        var enabled = context.IsPluginActive("ComputerUse");
        foreach (var definition in source)
        {
            if (RuntimeSafetyState.IsSafeMode) reasons[definition.Name] = SafeModeReason(definition.Name);
            else if (!enabled) reasons[definition.Name] = $"Tool '{definition.Name}' requires explicit @ComputerUse approval for this pass.";
            else if (!context.IsWindowsHost) reasons[definition.Name] = $"Tool '{definition.Name}' is unavailable because Computer Use currently requires Windows.";
            else Add(definition, ToolRuntimeKind.Computer, definitions, routes);
        }
        if (!RuntimeSafetyState.IsSafeMode && enabled && context.IsWindowsHost && source.Count > 0) plugins.Add("ComputerUse");
    }

    /// <summary>
    /// Performs the plan browser step owned by this component.
    /// </summary>
    private static void PlanBrowser(ToolAvailabilityContext context,
        IReadOnlyList<OllamaToolDefinition> background, IReadOnlyList<OllamaToolDefinition> interactive,
        List<OllamaToolDefinition> definitions, Dictionary<string, ToolRuntimeKind> routes,
        Dictionary<string, string> reasons, HashSet<string> plugins)
    {
        if (RuntimeSafetyState.IsSafeMode)
        {
            foreach (var definition in background.Concat(interactive)) reasons[definition.Name] = SafeModeReason(definition.Name);
            return;
        }
        var browserUse = context.IsPluginActive("BrowserUse");
        var webSearch = context.IsPluginActive("WebSearch");
        var any = browserUse || webSearch;
        var backgroundAllowed = (webSearch || browserUse && context.BrowserInteractiveHostAvailable) && context.BrowserHostAvailable && context.BrowserPermission != PermissionMode.Ask;
        var interactiveAllowed = browserUse && context.BrowserHostAvailable && context.BrowserInteractiveHostAvailable && context.BrowserPermission == PermissionMode.FullAccess;
        foreach (var definition in background)
        {
            if (backgroundAllowed) Add(definition, ToolRuntimeKind.Browser, definitions, routes);
            else reasons[definition.Name] = BrowserReason(definition.Name, any, context);
        }
        foreach (var definition in interactive)
        {
            if (interactiveAllowed) Add(definition, ToolRuntimeKind.Browser, definitions, routes);
            else if (!browserUse) reasons[definition.Name] = $"Tool '{definition.Name}' requires the interactive @BrowserUse plugin; @WebSearch is read-only.";
            else if (!context.BrowserHostAvailable) reasons[definition.Name] = $"Tool '{definition.Name}' is unavailable because no browser host is connected.";
            else if (!context.BrowserInteractiveHostAvailable) reasons[definition.Name] = $"Tool '{definition.Name}' requires the native Browse view to be open and attached.";
            else reasons[definition.Name] = $"Tool '{definition.Name}' requires Full Access browser permission or approval for this message.";
        }
        if (backgroundAllowed && browserUse && context.BrowserInteractiveHostAvailable) plugins.Add("BrowserUse");
        if (backgroundAllowed && webSearch) plugins.Add("WebSearch");
    }

    /// <summary>
    /// Performs the plan automations step owned by this component.
    /// </summary>
    private static void PlanAutomations(ToolAvailabilityContext context,
        IReadOnlyList<OllamaToolDefinition> automation, IReadOnlyList<OllamaToolDefinition> macros,
        List<OllamaToolDefinition> definitions, Dictionary<string, ToolRuntimeKind> routes,
        Dictionary<string, string> reasons, HashSet<string> plugins)
    {
        if (RuntimeSafetyState.IsSafeMode)
        {
            foreach (var definition in automation.Concat(macros)) reasons[definition.Name] = SafeModeReason(definition.Name);
            return;
        }
        var modeAllowed = context.Mode is HavenMode.Do or HavenMode.Studio;
        PlanAutomationGroup("Automate", context.IsPluginActive("Automate"), modeAllowed, context.AutomationHostAvailable, automation, definitions, routes, reasons, plugins);
        PlanAutomationGroup("Macro", context.IsPluginActive("Macro"), modeAllowed, context.AutomationHostAvailable, macros, definitions, routes, reasons, plugins);
    }

    /// <summary>
    /// Performs the plan automation group step owned by this component.
    /// </summary>
    private static void PlanAutomationGroup(string plugin, bool active, bool modeAllowed, bool host,
        IReadOnlyList<OllamaToolDefinition> source, List<OllamaToolDefinition> definitions,
        Dictionary<string, ToolRuntimeKind> routes, Dictionary<string, string> reasons, HashSet<string> plugins)
    {
        foreach (var definition in source)
        {
            if (!active) reasons[definition.Name] = $"Tool '{definition.Name}' requires @{plugin}.";
            else if (!modeAllowed) reasons[definition.Name] = $"Tool '{definition.Name}' is available only in Haven Do or Haven Studio.";
            else if (!host) reasons[definition.Name] = $"Tool '{definition.Name}' is unavailable because the local automation store is not connected.";
            else Add(definition, ToolRuntimeKind.Automation, definitions, routes);
        }
        if (active && modeAllowed && host && source.Count > 0) plugins.Add(plugin);
    }

    /// <summary>
    /// Performs the browser reason step owned by this component.
    /// </summary>
    private static string BrowserReason(string tool, bool any, ToolAvailabilityContext context)
    {
        if (!any) return $"Tool '{tool}' requires @WebSearch or @BrowserUse.";
        if (!context.BrowserHostAvailable) return $"Tool '{tool}' is unavailable because no browser host is connected.";
        if (context.IsPluginActive("BrowserUse") && !context.IsPluginActive("WebSearch") && !context.BrowserInteractiveHostAvailable)
            return $"Tool '{tool}' requires the native Browse view to be open when using @BrowserUse.";
        return $"Tool '{tool}' requires Auto Safe or Full Access browser permission, or approval for this message.";
    }

    /// <summary>
    /// Performs the safe mode reason step owned by this component.
    /// </summary>
    private static string SafeModeReason(string tool) =>
        $"Tool '{tool}' is disabled because Haven is in crash-loop recovery safe mode. {RuntimeSafetyState.Reason}";

    /// <summary>
    /// Performs the add step owned by this component.
    /// </summary>
    private static void Add(OllamaToolDefinition definition, ToolRuntimeKind runtime,
        List<OllamaToolDefinition> definitions, Dictionary<string, ToolRuntimeKind> routes)
    {
        if (!routes.TryAdd(definition.Name, runtime)) throw new InvalidOperationException($"Tool definition '{definition.Name}' is registered by more than one runtime.");
        definitions.Add(definition);
    }
}