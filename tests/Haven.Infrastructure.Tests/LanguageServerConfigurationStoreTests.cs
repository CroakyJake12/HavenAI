using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class LanguageServerConfigurationStoreTests : IDisposable
{
    private readonly TestPaths _paths = new();

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

    public void Dispose() => _paths.Dispose();

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

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
