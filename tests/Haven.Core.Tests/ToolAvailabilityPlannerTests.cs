using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ToolAvailabilityPlannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-tool-plan-tests", Guid.NewGuid().ToString("N"));
    private readonly ToolAvailabilityPlanner _planner = new();

    public ToolAvailabilityPlannerTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(HavenMode.Chat)]
    [InlineData(HavenMode.Teach)]
    public void WorkspaceToolsAreHiddenOutsideDoAndStudio(HavenMode mode)
    {
        var plan = Create(mode, file: PermissionMode.FullAccess, command: PermissionMode.FullAccess);

        Assert.DoesNotContain(plan.Definitions, definition => WorkspaceNames.Contains(definition.Name));
        Assert.Contains("only in Haven Do or Haven Studio", plan.GetUnavailableReason("read_file"));
    }

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

    [Fact]
    public void WorkspaceRequiresAnExistingRoot()
    {
        var missing = Path.Combine(_root, "missing");
        var plan = Create(HavenMode.Do, workspaceRoot: missing, file: PermissionMode.FullAccess, command: PermissionMode.FullAccess);

        Assert.DoesNotContain(plan.Definitions, definition => WorkspaceNames.Contains(definition.Name));
        Assert.Contains("folder that exists locally", plan.GetUnavailableReason("run_command"));
    }

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
        var plan = Create(HavenMode.Chat, [Plugin(plugin)], browser: permission, browserHost: hostAvailable,
            browserInteractiveHost: interactiveAvailable);

        Assert.Equal(expected, string.Join(',', plan.Definitions
            .Where(definition => definition.Name.StartsWith("browser_", StringComparison.Ordinal))
            .Select(definition => definition.Name)
            .OrderBy(name => name, StringComparer.Ordinal)));
    }

    [Fact]
    public void DetachedNativeBrowserKeepsWebSearchButHidesInteractiveBrowserUse()
    {
        var plan = Create(HavenMode.Chat, [Plugin("WebSearch"), Plugin("BrowserUse")],
            browser: PermissionMode.FullAccess, browserHost: true, browserInteractiveHost: false);

        Assert.Equal("browser_navigate,browser_read_page", string.Join(',', plan.Definitions.Select(item => item.Name).OrderBy(name => name, StringComparer.Ordinal)));
        Assert.True(plan.IsPluginAvailable("WebSearch"));
        Assert.False(plan.IsPluginAvailable("BrowserUse"));
    }

    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    public void ComputerUseRequiresBothExplicitEnablementAndWindows(
        bool pluginActive,
        bool windows,
        int expectedCount)
    {
        var plugins = pluginActive ? new[] { Plugin("ComputerUse") } : [];
        var plan = Create(HavenMode.Chat, plugins, windows: windows);

        Assert.Equal(expectedCount, plan.Definitions.Count(definition => definition.Name == "computer_snapshot"));
        Assert.Equal(expectedCount == 1, plan.IsPluginAvailable("ComputerUse"));
    }

    [Theory]
    [InlineData(HavenMode.Chat, "Automate", "", false)]
    [InlineData(HavenMode.Teach, "Macro", "", false)]
    [InlineData(HavenMode.Do, "Automate", "automation_create", true)]
    [InlineData(HavenMode.Studio, "Macro", "macro_create,macro_list", true)]
    public void AutomationAndMacroToolsAreModeAndPluginBound(
        HavenMode mode,
        string plugin,
        string expected,
        bool pluginAvailable)
    {
        var plan = Create(mode, [Plugin(plugin)]);

        Assert.Equal(expected, string.Join(',', plan.Definitions
            .Where(definition => definition.Name is "automation_create" or "macro_create" or "macro_list")
            .Select(definition => definition.Name)
            .OrderBy(name => name, StringComparer.Ordinal)));
        Assert.Equal(pluginAvailable, plan.IsPluginAvailable(plugin));
    }

    [Fact]
    public void ModelRestrictionKeepsOnlyRuntimeCapabilitiesTheModelSupports()
    {
        var plan = Create(HavenMode.Studio, [Plugin("BrowserUse"), Plugin("ComputerUse")],
            file: PermissionMode.FullAccess, command: PermissionMode.FullAccess, browser: PermissionMode.FullAccess, windows: true);
        var browserModel = new ModelDescriptor("browser-only", 1, "test", "test", "test",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Browser }, DateTimeOffset.UtcNow);

        var restricted = plan.RestrictToModel(browserModel);

        Assert.All(restricted.Definitions, definition => Assert.StartsWith("browser_", definition.Name));
        Assert.True(restricted.IsPluginAvailable("BrowserUse"));
        Assert.False(restricted.IsPluginAvailable("ComputerUse"));
        Assert.Contains("lacks the required tool capability", restricted.GetUnavailableReason("read_file"));
    }

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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private ToolAvailabilityPlan Create(
        HavenMode mode,
        IReadOnlyCollection<ActivePlugin>? plugins = null,
        string? workspaceRoot = null,
        PermissionMode file = PermissionMode.Ask,
        PermissionMode command = PermissionMode.Ask,
        PermissionMode browser = PermissionMode.Ask,
        bool windows = true,
        bool browserHost = true,
        bool browserInteractiveHost = true,
        bool automationHost = true) =>
        _planner.Create(
            new ToolAvailabilityContext(mode, workspaceRoot ?? _root, plugins ?? [], file, command, browser,
                windows, browserHost, browserInteractiveHost, automationHost),
            Sources);

    private static ActivePlugin Plugin(string name) => new(name, name, false);

    private static readonly HashSet<string> WorkspaceNames = new(StringComparer.Ordinal)
    {
        "list_files", "read_file", "search_files", "write_file", "replace_in_file", "run_command", "run_tests"
    };

    private static readonly ToolDefinitionSources Sources = new(
        WorkspaceNames.Select(Definition).ToArray(),
        [Definition("computer_snapshot")],
        [Definition("browser_navigate"), Definition("browser_read_page")],
        [Definition("browser_click"), Definition("browser_fill")],
        [Definition("automation_create")],
        [Definition("macro_create"), Definition("macro_list")]);

    private static OllamaToolDefinition Definition(string name) => new(name, name, new Dictionary<string, object>(), []);
}
