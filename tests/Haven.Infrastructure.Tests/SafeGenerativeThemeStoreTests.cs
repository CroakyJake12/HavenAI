/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/SafeGenerativeThemeStoreTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns SafeGenerativeThemeStoreTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents safe generative theme store tests and keeps its related state and behavior together.
/// </summary>
public sealed class SafeGenerativeThemeStoreTests : IDisposable
{
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the active theme lookup repairs missing theme and undefined appearance step owned by this component.
    /// </summary>
    [Fact]
    public async Task ActiveThemeLookupRepairsMissingThemeAndUndefinedAppearance()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var inner = new GenerativeThemeStore(_paths, new GenerativeThemeValidator(), diagnostics);
        var safe = new SafeGenerativeThemeStore(inner, diagnostics);
        _ = await inner.GetSelectionAsync(CancellationToken.None);

        var missingThemeId = Guid.NewGuid();
        var invalid = new GenerativeThemeSelection(
            1,
            missingThemeId,
            (GenerativeThemeAppearance)999,
            DateTimeOffset.UtcNow);
        var selectionPath = Path.Combine(_paths.DataDirectory, "GenerativeUi", "selection.json");
        await File.WriteAllTextAsync(
            selectionPath,
            JsonSerializer.Serialize(invalid, JsonOptions),
            CancellationToken.None);

        var active = await safe.GetActiveThemeAsync(CancellationToken.None);
        var repaired = await safe.GetSelectionAsync(CancellationToken.None);

        Assert.True(active.IsBuiltIn);
        Assert.Equal(active.Id, repaired.ActiveThemeId);
        Assert.Equal(GenerativeThemeAppearance.Dark, repaired.Appearance);
        Assert.NotEqual(missingThemeId, repaired.ActiveThemeId);
        var events = await diagnostics.ReadRecentAsync(20, CancellationToken.None);
        Assert.Contains(events, item => item.EventName == "selection-repaired");
    }

    /// <summary>
    /// Performs the public appearance mutations reject undefined values step owned by this component.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(999)]
    public async Task PublicAppearanceMutationsRejectUndefinedValues(int rawAppearance)
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var inner = new GenerativeThemeStore(_paths, new GenerativeThemeValidator(), diagnostics);
        var safe = new SafeGenerativeThemeStore(inner, diagnostics);
        var builtIn = (await safe.GetThemesAsync(CancellationToken.None)).First(theme => theme.IsBuiltIn);
        var appearance = (GenerativeThemeAppearance)rawAppearance;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            safe.SelectAsync(builtIn.Id, appearance, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            safe.SetAppearanceAsync(appearance, CancellationToken.None));
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
                "haven-safe-generative-theme-tests-" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);
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
            try
            {
                Directory.Delete(DataDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
