using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DashboardContractsTests
{
    [Fact]
    public void ManifestPolicyAcceptsOnlySandboxedProviderAndNavigationKeys()
    {
        var valid = new DashboardPluginTileManifest(
            "course-progress", "Course progress", "Upcoming lessons", "teach", "teaching", "teach", "Wide");

        DashboardTileManifestPolicy.ValidateForImport([valid]);

        var executableProvider = valid with { ProviderKey = "clr:Haven.Plugin.Run" };
        var arbitraryNavigation = valid with { ActionKey = "https://example.invalid" };
        var reservedReplacement = valid with { Key = "plan" };
        Assert.Throws<InvalidOperationException>(() => DashboardTileManifestPolicy.ValidateForImport([executableProvider]));
        Assert.Throws<InvalidOperationException>(() => DashboardTileManifestPolicy.ValidateForImport([arbitraryNavigation]));
        Assert.Throws<InvalidOperationException>(() => DashboardTileManifestPolicy.ValidateForImport([reservedReplacement]));
        Assert.Throws<InvalidOperationException>(() => DashboardTileManifestPolicy.ValidateForImport([valid, valid]));
    }

    [Fact]
    public void ProviderRegistryUsesStableKeysAndLastRegistrationWins()
    {
        var first = new FakeProvider(new("same", "First", "", "info", "action", "chat", DefaultOrder: 8));
        var second = new FakeProvider(new("same", "Second", "", "info", "action", "chat", DefaultOrder: 3));
        var other = new FakeProvider(new("other", "Other", "", "info", "action", "chat", DefaultOrder: 1));

        var registry = new DashboardTileProviderRegistry([first, other, second]);

        Assert.Equal(2, registry.Providers.Count);
        Assert.Same(other, registry.Providers[0]);
        Assert.Same(second, registry.Providers[1]);
    }

    private sealed class FakeProvider(DashboardTileDefinition definition) : IDashboardTileProvider
    {
        public DashboardTileDefinition Definition { get; } = definition;
        public Task<DashboardTileData> GetDataAsync(DashboardSnapshot snapshot, CancellationToken cancellationToken) =>
            Task.FromResult(new DashboardTileData("", ""));
    }
}
