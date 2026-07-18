/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/DashboardContractsTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns DashboardContractsTests, FakeProvider. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents dashboard contracts tests and keeps its related state and behavior together.
/// </summary>
public sealed class DashboardContractsTests
{
    /// <summary>
    /// Performs the manifest policy accepts only sandboxed provider and navigation keys step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the provider registry uses stable keys and last registration wins step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Represents fake provider and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeProvider(DashboardTileDefinition definition) : IDashboardTileProvider
    {
        /// <summary>
        /// Gets or updates definition, the bindable or domain state represented by this property.
        /// </summary>
        public DashboardTileDefinition Definition { get; } = definition;
        /// <summary>
        /// Retrieves data async for the current operation.
        /// </summary>
        public Task<DashboardTileData> GetDataAsync(DashboardSnapshot snapshot, CancellationToken cancellationToken) =>
            Task.FromResult(new DashboardTileData("", ""));
    }
}
