using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class ProviderConfigurationStoreSecurityTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Theory]
    [InlineData("https://user:password@api.example.test/v1/")]
    [InlineData("https://api.example.test/v1/?api_key=secret")]
    [InlineData("https://api.example.test/v1/#token")]
    public async Task EndpointCannotPersistCredentialsOrTokenComponents(string endpoint)
    {
        using var store = new ProviderConfigurationStore(_paths);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpsertAsync(Configuration(
                "custom",
                ModelProviderKind.OpenAICompatible,
                endpoint,
                enabled: true),
                CancellationToken.None));

        Assert.Contains("Credential Manager", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_paths.DataDirectory, "model-providers.json")));
    }

    [Fact]
    public async Task PublicPlainHttpEndpointIsRejected()
    {
        using var store = new ProviderConfigurationStore(_paths);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpsertAsync(Configuration(
                "custom",
                ModelProviderKind.OpenAICompatible,
                "http://api.example.test/v1/",
                enabled: true),
                CancellationToken.None));

        Assert.Contains("private-network", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CloudProviderCannotBePersistedAsLocal()
    {
        using var store = new ProviderConfigurationStore(_paths);
        await store.UpsertAsync(
            Configuration(
                "openai",
                ModelProviderKind.OpenAI,
                "https://api.openai.com/v1/",
                enabled: true,
                isLocal: true),
            CancellationToken.None);

        var loaded = await store.GetAsync("openai", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.False(loaded.IsLocal);
    }

    [Fact]
    public async Task LoopbackCompatibleProviderCanRemainLocal()
    {
        using var store = new ProviderConfigurationStore(_paths);
        await store.UpsertAsync(
            Configuration(
                "local-compatible",
                ModelProviderKind.OpenAICompatible,
                "http://127.0.0.1:8080/v1/",
                enabled: true,
                isLocal: true),
            CancellationToken.None);

        var loaded = await store.GetAsync("local-compatible", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.True(loaded.IsLocal);
        Assert.Equal("http://127.0.0.1:8080/v1/", loaded.Endpoint);
    }

    [Theory]
    [InlineData("api_key")]
    [InlineData("access-token")]
    [InlineData("Authorization")]
    public async Task SecretLikeMetadataKeysAreRejected(string key)
    {
        using var store = new ProviderConfigurationStore(_paths);
        var configuration = Configuration(
            "custom",
            ModelProviderKind.OpenAICompatible,
            "https://api.example.test/v1/",
            enabled: true) with
        {
            Metadata = new Dictionary<string, string> { [key] = "must-not-be-json" }
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpsertAsync(configuration, CancellationToken.None));

        Assert.Contains("looks secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidPrimaryStoreIsQuarantinedAndBuiltInsRemainAvailable()
    {
        var path = Path.Combine(_paths.DataDirectory, "model-providers.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new[]
        {
            Configuration(
                "custom",
                ModelProviderKind.OpenAICompatible,
                "https://user:password@api.example.test/v1/",
                enabled: true)
        }));
        using var store = new ProviderConfigurationStore(_paths);

        var configurations = await store.GetAllAsync(CancellationToken.None);

        Assert.Contains(configurations, item => item.Id == "ollama");
        Assert.Contains(configurations, item => item.Id == "openai");
        Assert.DoesNotContain(configurations, item => item.Id == "custom");
        Assert.False(File.Exists(path));
        Assert.NotEmpty(Directory.GetFiles(_paths.DataDirectory, "model-providers.json.corrupt-*"));
    }

    private static ProviderConfiguration Configuration(
        string id,
        ModelProviderKind kind,
        string endpoint,
        bool enabled,
        bool isLocal = false) => new(
        id,
        kind,
        id,
        endpoint,
        enabled,
        isLocal,
        false,
        new Dictionary<string, string>(),
        DateTimeOffset.UtcNow);

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(
                Path.GetTempPath(),
                "haven-provider-config-security-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "legacy.json");
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (IOException) { }
        }
    }
}
