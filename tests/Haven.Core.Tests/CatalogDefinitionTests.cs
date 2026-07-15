using System.Text.Json;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class CatalogDefinitionTests
{
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
