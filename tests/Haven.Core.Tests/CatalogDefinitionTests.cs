/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/CatalogDefinitionTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns CatalogDefinitionTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents catalog definition tests and keeps its related state and behavior together.
/// </summary>
public sealed class CatalogDefinitionTests
{
    /// <summary>
    /// Performs the functional plugins and prompt library have expected built ins step owned by this component.
    /// </summary>
    [Fact]
    public void FunctionalPluginsAndPromptLibraryHaveExpectedBuiltIns()
    {
        var pluginNames = PluginCatalog.BuiltIns.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(new[] { "Agent", "Automate", "BrowserUse", "ComputerUse", "DuoMode", "Goal", "Macro", "Test", "WebSearch" },
            pluginNames.OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain("Parameter", pluginNames);

        var promptNames = PromptCatalog.BuiltIns.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expected in new[] { "Report", "Inspect", "Rapid", "Explain", "Experiment", "GoldenRules", "Rigid", "StressTest", "Context", "Handoff" })
            Assert.Contains(expected, promptNames);
    }

    /// <summary>
    /// Performs the agentic workspace plugins are mode bound and web search uses browser capability step owned by this component.
    /// </summary>
    [Fact]
    public void AgenticWorkspacePluginsAreModeBoundAndWebSearchUsesBrowserCapability()
    {
        foreach (var name in new[] { "DuoMode", "Automate", "Test", "Macro" })
        {
            var plugin = Assert.Single(PluginCatalog.BuiltIns, item => item.Name == name);
            var modes = JsonSerializer.Deserialize<string[]>(plugin.AllowedModesJson);
            Assert.NotNull(modes);
            Assert.Contains("Do", modes!);
            Assert.Contains("Studio", modes!);
        }

        var webSearch = Assert.Single(PluginCatalog.BuiltIns, item => item.Name == "WebSearch");
        Assert.Contains("Browser", webSearch.CapabilitiesJson);
    }
}
