using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class RecoverySafeModePolicyTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "haven-safe-mode-policy-" + Guid.NewGuid().ToString("N"));

    public RecoverySafeModePolicyTests() => Directory.CreateDirectory(_workspace);

    [Fact]
    public void SafeModeKeepsReadOnlyWorkspaceToolsAndBlocksEverySideEffectRuntime()
    {
        RuntimeSafetyState.EnableSafeMode("test crash loop");
        var context = new ToolAvailabilityContext(
            HavenMode.Do,
            _workspace,
            [],
            PermissionMode.FullAccess,
            PermissionMode.FullAccess,
            PermissionMode.FullAccess,
            IsWindowsHost: true,
            BrowserHostAvailable: true,
            BrowserInteractiveHostAvailable: true,
            AutomationHostAvailable: true);
        var sources = new ToolDefinitionSources(
            [Definition("read_file"), Definition("write_file"), Definition("run_command")],
            [Definition("computer_click")],
            [Definition("browser_navigate")],
            [Definition("browser_click_ref")],
            [Definition("automation_run")],
            [Definition("macro_run")]);

        var plan = ToolAvailabilityPlanner.Default.Create(context, sources);

        Assert.Equal(["read_file"], plan.Definitions.Select(item => item.Name).ToArray());
        foreach (var name in new[]
                 {
                     "write_file", "run_command", "computer_click", "browser_navigate",
                     "browser_click_ref", "automation_run", "macro_run"
                 })
        {
            Assert.Contains("safe mode", plan.GetUnavailableReason(name), StringComparison.OrdinalIgnoreCase);
        }
        Assert.False(plan.IsPluginAvailable("ComputerUse"));
        Assert.False(plan.IsPluginAvailable("BrowserUse"));
        Assert.False(plan.IsPluginAvailable("Automate"));
    }

    [Fact]
    public void NormalModeRestoresTheExistingPermissionPlanner()
    {
        RuntimeSafetyState.DisableSafeMode();
        var context = new ToolAvailabilityContext(
            HavenMode.Do,
            _workspace,
            [],
            PermissionMode.FullAccess,
            PermissionMode.FullAccess,
            PermissionMode.FullAccess,
            IsWindowsHost: true,
            BrowserHostAvailable: false,
            BrowserInteractiveHostAvailable: false,
            AutomationHostAvailable: false);
        var sources = new ToolDefinitionSources(
            [Definition("read_file"), Definition("write_file"), Definition("run_command")],
            [], [], [], [], []);

        var plan = ToolAvailabilityPlanner.Default.Create(context, sources);

        Assert.Equal(
            new[] { "read_file", "write_file", "run_command" },
            plan.Definitions.Select(item => item.Name).ToArray());
    }

    public void Dispose()
    {
        RuntimeSafetyState.DisableSafeMode();
        try { Directory.Delete(_workspace, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static OllamaToolDefinition Definition(string name) =>
        new(name, "test", new Dictionary<string, object>(), []);
}
