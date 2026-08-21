using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class ProviderEndpointSecurityTests
{
    [Theory]
    [InlineData("http://example.com/v1")]
    [InlineData("ftp://example.com/v1")]
    [InlineData("relative/path")]
    public async Task Remote_or_invalid_insecure_endpoints_are_rejected_at_transport_boundary(string endpoint)
    {
        var store = new Store(Configuration(endpoint));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProviderHttp.RequireEnabledAsync(store, "provider", "https://fallback.example/v1", CancellationToken.None));
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434/v1")]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("https://example.com/v1")]
    public async Task Loopback_http_and_remote_https_are_allowed(string endpoint)
    {
        var configured = await ProviderHttp.RequireEnabledAsync(
            new Store(Configuration(endpoint)),
            "provider",
            "https://fallback.example/v1",
            CancellationToken.None);

        Assert.EndsWith("/", configured.Endpoint, StringComparison.Ordinal);
    }

    private static ProviderConfiguration Configuration(string endpoint) => new(
        "provider",
        ModelProviderKind.OpenAICompatible,
        "Provider",
        endpoint,
        IsEnabled: true,
        IsLocal: false,
        AllowCloudFallback: false,
        new Dictionary<string, string>(),
        DateTimeOffset.UtcNow);

    private sealed class Store(ProviderConfiguration configuration) : IProviderConfigurationStore
    {
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderConfiguration>>([configuration]);

        public Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken cancellationToken) =>
            Task.FromResult<ProviderConfiguration?>(configuration.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase) ? configuration : null);

        public Task UpsertAsync(ProviderConfiguration value, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
