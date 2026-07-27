/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/BrowserSitePermissionCapacityTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns BrowserSitePermissionCapacityTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Browser;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents browser site permission capacity tests and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserSitePermissionCapacityTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the loading more than capacity keeps newest permissions step owned by this component.
    /// </summary>
    [Fact]
    public void LoadingMoreThanCapacityKeepsNewestPermissions()
    {
        var start = DateTimeOffset.UtcNow.AddDays(-10);
        var permissions = Enumerable.Range(0, 501)
            .Select(index => new
            {
                origin = $"https://site-{index}.example",
                kind = (int)BrowserSitePermissionKind.Notifications,
                decision = (int)BrowserSitePermissionDecision.Allow,
                updatedAt = start.AddMinutes(index)
            })
            .ToArray();
        File.WriteAllText(
            Path.Combine(_paths.DataDirectory, "browser-site-permissions.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                permissions,
                audit = Array.Empty<object>()
            }));

        using var store = new BrowserSitePermissionStore(_paths);

        Assert.Equal(500, store.Permissions.Count);
        Assert.DoesNotContain(store.Permissions, item => item.Origin == "https://site-0.example");
        Assert.Contains(store.Permissions, item => item.Origin == "https://site-500.example");
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(
                Path.GetTempPath(),
                "haven-browser-permission-capacity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
        }

        /// <summary>
        /// Gets or updates data directory, the bindable or domain state represented by this property.
        /// </summary>
        public string DataDirectory { get; }
        /// <summary>
        /// Gets or updates database path, the bindable or domain state represented by this property.
        /// </summary>
        public string DatabasePath { get; }
        /// <summary>
        /// Gets or updates browser profile directory, the bindable or domain state represented by this property.
        /// </summary>
        public string BrowserProfileDirectory { get; }
        /// <summary>
        /// Gets or updates attachments directory, the bindable or domain state represented by this property.
        /// </summary>
        public string AttachmentsDirectory { get; }
        /// <summary>
        /// Gets or updates logs directory, the bindable or domain state represented by this property.
        /// </summary>
        public string LogsDirectory { get; }
        /// <summary>
        /// Gets or updates legacy state path, the bindable or domain state represented by this property.
        /// </summary>
        public string LegacyStatePath { get; }

        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (IOException) { }
        }
    }
}
