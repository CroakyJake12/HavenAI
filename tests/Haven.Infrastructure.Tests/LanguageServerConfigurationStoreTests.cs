/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/LanguageServerConfigurationStoreTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns LanguageServerConfigurationStoreTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents language server configuration store tests and keeps its related state and behavior together.
/// </summary>
public sealed class LanguageServerConfigurationStoreTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the built in definitions are created disabled step owned by this component.
    /// </summary>
    [Fact]
    public async Task BuiltInDefinitionsAreCreatedDisabled()
    {
        var store = new LanguageServerConfigurationStore(_paths);

        var definitions = await store.GetAllAsync(CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.All(definitions, definition => Assert.False(definition.IsEnabled));
        Assert.Contains(definitions, definition => definition.Id == "csharp-ls" && definition.Extensions.Contains(".cs"));
        Assert.True(File.Exists(Path.Combine(_paths.DataDirectory, "language-servers.json")));
    }

    /// <summary>
    /// Performs the upsert normalizes extensions and finds enabled server by path step owned by this component.
    /// </summary>
    [Fact]
    public async Task UpsertNormalizesExtensionsAndFindsEnabledServerByPath()
    {
        var store = new LanguageServerConfigurationStore(_paths);
        var definition = new LanguageServerDefinition(
            "  CUSTOM  ",
            " Custom Server ",
            " dotnet ",
            " --info ",
            " custom-language ",
            ["csx", ".CSX", " .custom "],
            true,
            500,
            "{\"setting\":true}");

        await store.UpsertAsync(definition, CancellationToken.None);
        var found = await store.FindForPathAsync("example.CSX", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("custom", found!.Id);
        Assert.Equal([".csx", ".custom"], found.Extensions);
        Assert.Equal(120, found.RequestTimeoutSeconds);
        Assert.Contains("\"setting\": true", found.InitializationOptionsJson);
    }

    /// <summary>
    /// Performs the delete removes only requested definition step owned by this component.
    /// </summary>
    [Fact]
    public async Task DeleteRemovesOnlyRequestedDefinition()
    {
        var store = new LanguageServerConfigurationStore(_paths);
        await store.GetAllAsync(CancellationToken.None);

        await store.DeleteAsync("pylsp", CancellationToken.None);
        var values = await store.GetAllAsync(CancellationToken.None);

        Assert.DoesNotContain(values, item => item.Id == "pylsp");
        Assert.Contains(values, item => item.Id == "csharp-ls");
    }

    /// <summary>
    /// Performs the corrupt settings are quarantined and safe defaults are restored step owned by this component.
    /// </summary>
    [Fact]
    public async Task CorruptSettingsAreQuarantinedAndSafeDefaultsAreRestored()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var path = Path.Combine(_paths.DataDirectory, "language-servers.json");
        await File.WriteAllTextAsync(path, "{ definitely not json");
        var store = new LanguageServerConfigurationStore(_paths);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAllAsync(CancellationToken.None));

        Assert.Contains("corrupt", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path));
        Assert.NotEmpty(Directory.EnumerateFiles(_paths.DataDirectory, "language-servers.json.corrupt.*.json"));
        var restored = await store.GetAllAsync(CancellationToken.None);
        Assert.All(restored, definition => Assert.False(definition.IsEnabled));
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
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-language-server-settings-tests-" + Guid.NewGuid().ToString("N"));
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
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
