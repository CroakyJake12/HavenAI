/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/RecoverySafeModePolicyTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns RecoverySafeModePolicyTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents recovery safe mode policy tests and keeps its related state and behavior together.
/// </summary>
public sealed class RecoverySafeModePolicyTests : IDisposable
{
    /// <summary>
    /// Stores workspace locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "haven-safe-mode-policy-" + Guid.NewGuid().ToString("N"));

    public RecoverySafeModePolicyTests() => Directory.CreateDirectory(_workspace);

    /// <summary>
    /// Performs the safe mode keeps read only workspace tools and blocks every side effect runtime step owned by this component.
    /// </summary>
    [Fact]
    public void SafeModeKeepsReadOnlyWorkspaceToolsAndBlocksEverySideEffectRuntime()
    {
        RuntimeSafetyState.EnableSafeMode("test crash loop");
        var context = new ToolAvailabilityContext(
            HavenMode.Tasks,
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
            [Definition("task_run")]);

        var plan = ToolAvailabilityPlanner.Default.Create(context, sources);

        Assert.Equal(["read_file"], plan.Definitions.Select(item => item.Name).ToArray());
        foreach (var name in new[]
                 {
                     "write_file", "run_command", "computer_click", "browser_navigate",
                     "browser_click_ref", "automation_run", "task_run"
                 })
        {
            Assert.Contains("safe mode", plan.GetUnavailableReason(name), StringComparison.OrdinalIgnoreCase);
        }
        Assert.False(plan.IsCapabilityAvailable("computer-device-use"));
        Assert.False(plan.IsCapabilityAvailable("browser-use"));
        Assert.False(plan.IsCapabilityAvailable("create-automation"));
    }

    /// <summary>
    /// Performs the normal mode restores the existing permission planner step owned by this component.
    /// </summary>
    [Fact]
    public void NormalModeRestoresTheExistingPermissionPlanner()
    {
        RuntimeSafetyState.DisableSafeMode();
        var context = new ToolAvailabilityContext(
            HavenMode.Tasks,
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        RuntimeSafetyState.DisableSafeMode();
        try { Directory.Delete(_workspace, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Performs the definition step owned by this component.
    /// </summary>
    private static OllamaToolDefinition Definition(string name) =>
        new(name, "test", new Dictionary<string, object>(), []);
}
