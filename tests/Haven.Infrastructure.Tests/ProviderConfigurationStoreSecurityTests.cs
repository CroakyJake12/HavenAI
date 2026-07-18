/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/ProviderConfigurationStoreSecurityTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ProviderConfigurationStoreSecurityTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents provider configuration store security tests and keeps its related state and behavior together.
/// </summary>
public sealed class ProviderConfigurationStoreSecurityTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the endpoint cannot persist credentials or token components step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the public plain http endpoint is rejected step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the cloud provider cannot be persisted as local step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the loopback compatible provider can remain local step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the secret like metadata keys are rejected step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the invalid primary store is quarantined and built ins remain available step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the configuration step owned by this component.
    /// </summary>
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
                "haven-provider-config-security-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "legacy.json");
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
