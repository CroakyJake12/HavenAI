using Haven.Core;

namespace Haven.Core.Tests;

public sealed class CatalogDefinitionTests
{
    [Fact]
    public void CapabilityRegistryAndPromptLibraryHaveExpectedBuiltIns()
    {
        var capabilityKeys = CapabilityRegistryCatalog.BuiltIns
            .Select(item => item.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in new[]
                 {
                     "web-search", "browser-use", "create-automation", "run-task", "edit-task",
                     "computer-device-use", "run-command", "run-tests"
                 })
            Assert.Contains(expected, capabilityKeys);

        Assert.DoesNotContain("parameter", capabilityKeys);

        var promptNames = PromptCatalog.BuiltIns
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expected in new[]
                 {
                     "Report", "Inspect", "Rapid", "Explain", "Experiment", "GoldenRules", "Rigid",
                     "StressTest", "Context", "Handoff"
                 })
            Assert.Contains(expected, promptNames);
    }

    [Fact]
    public void ReplacementCapabilitiesMapToCurrentProvidersAndPlatforms()
    {
        var createAutomation = Assert.Single(CapabilityRegistryCatalog.BuiltIns, item => item.Key == "create-automation");
        Assert.Equal("haven.tasks", createAutomation.ProviderId);
        Assert.Equal("tasks.automation.create", createAutomation.ImplementationKey);
        Assert.True(createAutomation.IsAttachable);
        Assert.True(createAutomation.IsAgentUsable);

        var webSearch = Assert.Single(CapabilityRegistryCatalog.BuiltIns, item => item.Key == "web-search");
        Assert.Equal("haven.browser", webSearch.ProviderId);
        Assert.Equal(CapabilityAvailability.PermissionRequired, webSearch.Availability);

        var runTests = Assert.Single(CapabilityRegistryCatalog.BuiltIns, item => item.Key == "run-tests");
        Assert.Equal(CapabilityPlatform.Windows, runTests.Platforms);
        Assert.Equal("haven.workspace", runTests.ProviderId);
    }
}
