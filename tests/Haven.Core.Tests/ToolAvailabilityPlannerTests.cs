/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/ToolAvailabilityPlannerTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ToolAvailabilityPlannerTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents tool availability planner tests and keeps its related state and behavior together.
/// </summary>
public sealed class ToolAvailabilityPlannerTests : IDisposable
{
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-tool-plan-tests", Guid.NewGuid().ToString("N"));
    /// <summary>
    /// Stores planner locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ToolAvailabilityPlanner _planner = new();

    public ToolAvailabilityPlannerTests() => Directory.CreateDirectory(_root);

    /// <summary>
    /// Performs the workspace tools are hidden outside Tasks and Studio step owned by this component.
    /// </summary>
    [Theory]
    [InlineData(HavenMode.Chat)]
    [InlineData(HavenMode.Study)]
    public void WorkspaceToolsAreHiddenOutsideTasksAndStudio(HavenMode mode)
    {
        var plan = Create(mode, file: PermissionMode.FullAccess, command: PermissionMode.FullAccess);

        Assert.DoesNotContain(plan.Definitions, definition => WorkspaceNames.Contains(definition.Name));
        Assert.Contains("only in Haven Tasks or Haven Studio", plan.GetUnavailableReason("read_file"));
    }

    /// <summary>
    /// Performs the workspace permission matrix exposes only approved operations step owned by this component.
    /// </summary>
    [Theory]
    [InlineData(PermissionMode.Ask, PermissionMode.Ask, "list_files,read_file,search_files")]
    [InlineData(PermissionMode.AutoSafe, PermissionMode.AutoSafe, "list_files,read_file,replace_in_file,run_tests,search_files,write_file")]
    [InlineData(PermissionMode.FullAccess, PermissionMode.FullAccess, "list_files,read_file,replace_in_file,run_command,run_tests,search_files,write_file")]
    public void WorkspacePermissionMatrixExposesOnlyApprovedOperations(
        PermissionMode file,
        PermissionMode command,
        string expected)
    {
        var plan = Create(HavenMode.Studio, file: file, command: command);

        Assert.Equal(expected, string.Join(',', plan.Definitions
            .Where(definition => WorkspaceNames.Contains(definition.Name))
            .Select(definition => definition.Name)
            .OrderBy(name => name, StringComparer.Ordinal)));
    }

    /// <summary>
    /// Performs the workspace requires an existing root step owned by this component.
    /// </summary>
    [Fact]
    public void WorkspaceRequiresAnExistingRoot()
    {
        var missing = Path.Combine(_root, "missing");
        var plan = Create(HavenMode.Tasks, workspaceRoot: missing, file: PermissionMode.FullAccess, command: PermissionMode.FullAccess);

        Assert.DoesNotContain(plan.Definitions, definition => WorkspaceNames.Contains(definition.Name));
        Assert.Contains("folder that exists locally", plan.GetUnavailableReason("run_command"));
    }

    /// <summary>
    /// Performs the browser matrix separates background and interactive tools step owned by this component.
    /// </summary>
    [Theory]
    [InlineData("WebSearch", PermissionMode.FullAccess, true, true, "browser_navigate,browser_read_page")]
    [InlineData("BrowserUse", PermissionMode.AutoSafe, true, true, "browser_navigate,browser_read_page")]
    [InlineData("BrowserUse", PermissionMode.FullAccess, true, true, "browser_click,browser_fill,browser_navigate,browser_read_page")]
    [InlineData("BrowserUse", PermissionMode.FullAccess, true, false, "")]
    [InlineData("BrowserUse", PermissionMode.Ask, true, true, "")]
    [InlineData("BrowserUse", PermissionMode.FullAccess, false, false, "")]
    public void BrowserMatrixSeparatesBackgroundAndInteractiveTools(
        string plugin,
        PermissionMode permission,
        bool hostAvailable,
        bool interactiveAvailable,
        string expected)
    {
        var plan = Create(HavenMode.Chat, [Capability(plugin)], browser: permission, browserHost: hostAvailable,
            browserInteractiveHost: interactiveAvailable);

        Assert.Equal(expected, string.Join(',', plan.Definitions
            .Where(definition => definition.Name.StartsWith("browser_", StringComparison.Ordinal))
            .Select(definition => definition.Name)
            .OrderBy(name => name, StringComparer.Ordinal)));
    }

    /// <summary>
    /// Performs the detached native browser keeps web search but hides interactive browser use step owned by this component.
    /// </summary>
    [Fact]
    public void DetachedNativeBrowserKeepsWebSearchButHidesInteractiveBrowserUse()
    {
        var plan = Create(HavenMode.Chat, [Capability("WebSearch"), Capability("BrowserUse")],
            browser: PermissionMode.FullAccess, browserHost: true, browserInteractiveHost: false);

        Assert.Equal("browser_navigate,browser_read_page", string.Join(',', plan.Definitions.Select(item => item.Name).OrderBy(name => name, StringComparer.Ordinal)));
        Assert.True(plan.IsCapabilityAvailable("web-search"));
        Assert.False(plan.IsCapabilityAvailable("browser-use"));
    }

    /// <summary>
    /// Performs the computer use requires both explicit enablement and windows step owned by this component.
    /// </summary>
    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    public void ComputerUseRequiresBothExplicitEnablementAndWindows(
        bool pluginActive,
        bool windows,
        int expectedCount)
    {
        var capabilities = pluginActive ? new[] { Capability("ComputerUse") } : [];
        var plan = Create(HavenMode.Chat, capabilities, windows: windows);

        Assert.Equal(expectedCount, plan.Definitions.Count(definition => definition.Name == "computer_snapshot"));
        Assert.Equal(expectedCount == 1, plan.IsCapabilityAvailable("computer-device-use"));
    }

    /// <summary>
    /// Performs the scheduled-action and reusable-task tools are mode and plugin bound step owned by this component.
    /// </summary>
    [Theory]
    [InlineData(HavenMode.Chat, "Automate", "", false)]
    [InlineData(HavenMode.Study, "Automate", "", false)]
    [InlineData(HavenMode.Tasks, "Automate", "automation_create,task_create,task_list", true)]
    [InlineData(HavenMode.Studio, "Automate", "automation_create,task_create,task_list", true)]
    public void TaskToolsAreModeAndPluginBound(
        HavenMode mode,
        string plugin,
        string expected,
        bool pluginAvailable)
    {
        var plan = Create(mode, [Capability(plugin)]);

        Assert.Equal(expected, string.Join(',', plan.Definitions
            .Where(definition => definition.Name is "automation_create" or "task_create" or "task_list")
            .Select(definition => definition.Name)
            .OrderBy(name => name, StringComparer.Ordinal)));
        Assert.Equal(pluginAvailable, plan.IsCapabilityAvailable(Capability(plugin).Key));
    }

    /// <summary>
    /// Performs the model restriction keeps only runtime capabilities the model supports step owned by this component.
    /// </summary>
    [Fact]
    public void ModelRestrictionKeepsOnlyRuntimeCapabilitiesTheModelSupports()
    {
        var plan = Create(HavenMode.Studio, [Capability("BrowserUse"), Capability("ComputerUse")],
            file: PermissionMode.FullAccess, command: PermissionMode.FullAccess, browser: PermissionMode.FullAccess, windows: true);
        var browserModel = new ModelDescriptor("browser-only", 1, "test", "test", "test",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Browser }, DateTimeOffset.UtcNow);

        var restricted = plan.RestrictToModel(browserModel);

        Assert.All(restricted.Definitions, definition => Assert.StartsWith("browser_", definition.Name));
        Assert.True(restricted.IsCapabilityAvailable("browser-use"));
        Assert.False(restricted.IsCapabilityAvailable("computer-device-use"));
        Assert.Contains("lacks the required tool capability", restricted.GetUnavailableReason("read_file"));
    }

    /// <summary>
    /// Performs the routes are exact and unavailable errors explain the boundary step owned by this component.
    /// </summary>
    [Fact]
    public void RoutesAreExactAndUnavailableErrorsExplainTheBoundary()
    {
        var plan = Create(HavenMode.Studio, file: PermissionMode.Ask, command: PermissionMode.Ask);

        Assert.True(plan.TryGetRuntime("read_file", out var runtime));
        Assert.Equal(ToolRuntimeKind.Workspace, runtime);
        Assert.False(plan.TryGetRuntime("read_file_extra", out _));
        Assert.Contains("not registered", plan.GetUnavailableReason("read_file_extra"));
        Assert.Contains("requires Auto Safe or Full Access", plan.GetUnavailableReason("write_file"));
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    /// <summary>
    /// Creates this member with the invariants required by its callers.
    /// </summary>
    private ToolAvailabilityPlan Create(
        HavenMode mode,
        IReadOnlyCollection<ActiveCapability>? capabilities = null,
        string? workspaceRoot = null,
        PermissionMode file = PermissionMode.Ask,
        PermissionMode command = PermissionMode.Ask,
        PermissionMode browser = PermissionMode.Ask,
        bool windows = true,
        bool browserHost = true,
        bool browserInteractiveHost = true,
        bool automationHost = true) =>
        _planner.Create(
            new ToolAvailabilityContext(mode, workspaceRoot ?? _root, capabilities ?? [], file, command, browser,
                windows, browserHost, browserInteractiveHost, automationHost),
            Sources);

    /// <summary>
    /// Creates a temporary Classic capability for compatibility tests.
    /// </summary>
    private static ActiveCapability Capability(string name) => ActiveCapability.FromLegacyPlugin(name, name);

    /// <summary>
    /// Stores workspace names locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> WorkspaceNames = new(StringComparer.Ordinal)
    {
        "list_files", "read_file", "search_files", "write_file", "replace_in_file", "run_command", "run_tests"
    };

    /// <summary>
    /// Stores sources locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly ToolDefinitionSources Sources = new(
        WorkspaceNames.Select(Definition).ToArray(),
        [Definition("computer_snapshot")],
        [Definition("browser_navigate"), Definition("browser_read_page")],
        [Definition("browser_click"), Definition("browser_fill")],
        [Definition("automation_create")],
        [Definition("task_create"), Definition("task_list")]);

    /// <summary>
    /// Performs the definition step owned by this component.
    /// </summary>
    private static OllamaToolDefinition Definition(string name) => new(name, name, new Dictionary<string, object>(), []);
}
