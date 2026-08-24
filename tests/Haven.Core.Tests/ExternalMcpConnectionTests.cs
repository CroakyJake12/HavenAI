using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ExternalMcpConnectionTests
{
    [Fact]
    public async Task UefnPresetUsesGenericMcpRegistryAndPersistsNegotiatedIdentity()
    {
        var repository = new MemoryConnectionRepository();
        var client = new FakeMcpClient
        {
            Identity = new McpServerIdentity("unreal-mcp", "1.0", "2026-07-28", "{}"),
            Tools = [Tool("read_verse", "Read Verse files")]
        };
        var service = new ExternalConnectionRegistryService(repository, client);

        var connection = await service.ConnectUefnAsync(null, CancellationToken.None);

        Assert.Equal(ExternalConnectionKind.Mcp, connection.Kind);
        Assert.Equal("uefn", connection.PresetKey);
        Assert.Equal(ExternalConnectionState.Ready, connection.State);
        Assert.Equal("unreal-mcp", connection.ServerName);
        Assert.Equal("2026-07-28", connection.ProtocolVersion);
        var config = JsonSerializer.Deserialize<McpConnectionConfiguration>(connection.ConfigurationJson)!;
        Assert.Equal("http://127.0.0.1:8000/mcp", config.Endpoint);
        Assert.True(config.LocalOnly);
        Assert.True(config.SerializeInvocations);
        Assert.Single(await repository.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UefnRejectsNonUnrealServerIdentityWithoutDeletingSavedConnection()
    {
        var repository = new MemoryConnectionRepository();
        var client = new FakeMcpClient { Identity = new McpServerIdentity("some-other-server", "1", "2026-07-28", "{}") };
        var service = new ExternalConnectionRegistryService(repository, client);

        var connection = await service.ConnectUefnAsync(null, CancellationToken.None);

        Assert.Equal(ExternalConnectionState.Offline, connection.State);
        Assert.Contains("unreal-mcp", connection.Status, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await repository.GetAsync(connection.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData("http://example.com/mcp")]
    [InlineData("ftp://example.com/mcp")]
    public void UnsafeRemoteMcpEndpointsAreRejected(string endpoint)
    {
        var config = new McpConnectionConfiguration(McpTransportKind.StreamableHttp, endpoint);
        Assert.Throws<InvalidOperationException>(() => ExternalConnectionRegistryService.ValidateMcpConfiguration(config, false));
    }

    [Fact]
    public async Task AttachedMcpToolPreservesRawSchemaAndInvokesThroughSharedRuntime()
    {
        var repository = new MemoryConnectionRepository();
        var connection = ReadyConnection("UEFN");
        await repository.UpsertAsync(connection, CancellationToken.None);
        var schema = Element("{\"type\":\"object\",\"properties\":{\"path\":{\"$ref\":\"#/$defs/path\"}},\"$defs\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}");
        var client = new FakeMcpClient { Tools = [new McpExternalTool("read_verse", "Read Verse", schema)] };
        var runtime = new McpToolRuntime(repository, client);
        var active = Active(connection);

        var definitions = await runtime.GetDefinitionsAsync([active], CancellationToken.None);
        var definition = Assert.Single(definitions);
        Assert.NotNull(definition.InputSchema);
        Assert.True(definition.InputSchema!.Value.TryGetProperty("$defs", out _));
        var call = new OllamaToolCall(definition.Name, new Dictionary<string, JsonElement> { ["path"] = Element("\"/Verse/Test.verse\"") });
        var result = await runtime.ExecuteAsync(call, [active], PermissionMode.Ask, CancellationToken.None);

        Assert.True(result.Activity.Succeeded);
        Assert.Equal(1, client.InvocationCount);
        Assert.Equal("read_verse", client.LastToolName);
    }

    [Fact]
    public async Task MutatingMcpToolRequiresPermissionAndNeverFallsBackToAnotherRuntime()
    {
        var repository = new MemoryConnectionRepository();
        var connection = ReadyConnection("UEFN");
        await repository.UpsertAsync(connection, CancellationToken.None);
        var client = new FakeMcpClient { Tools = [Tool("write_verse", "Write a Verse file")] };
        var runtime = new McpToolRuntime(repository, client);
        var active = Active(connection);
        var definition = Assert.Single(await runtime.GetDefinitionsAsync([active], CancellationToken.None));

        var result = await runtime.ExecuteAsync(new OllamaToolCall(definition.Name, new Dictionary<string, JsonElement>()), [active], PermissionMode.Ask, CancellationToken.None);

        Assert.False(result.Activity.Succeeded);
        Assert.Contains("requires approval", result.Output, StringComparison.OrdinalIgnoreCase);
        var failure = Assert.IsType<ToolFailureDescriptor>(result.Failure);
        Assert.Equal(ToolFailureKind.PermissionRequired, failure.Kind);
        Assert.Equal(RemediationType.PermissionRequest, failure.SuggestedRemediation);
        Assert.True(failure.Retryable);
        Assert.True(failure.Risk.ExpandsPermissions);
        Assert.Equal(ExternalConnectionNaming.CapabilityKey(connection.Id), failure.ComponentId);
        Assert.Equal(0, client.InvocationCount);
    }

    [Fact]
    public async Task DisabledOrUnattachedConnectionContributesNoExecutableTools()
    {
        var repository = new MemoryConnectionRepository();
        var connection = ReadyConnection("Local Build") with { IsEnabled = false, State = ExternalConnectionState.Disabled };
        await repository.UpsertAsync(connection, CancellationToken.None);
        var runtime = new McpToolRuntime(repository, new FakeMcpClient { Tools = [Tool("build", "Build")] });
        Assert.Empty(await runtime.GetDefinitionsAsync([Active(connection)], CancellationToken.None));
        Assert.Empty(await runtime.GetDefinitionsAsync([], CancellationToken.None));
    }

    [Fact]
    public async Task RemoveDeletesSecureOAuthTokensAndRegistryRecord()
    {
        var repository = new MemoryConnectionRepository();
        var secrets = new MemorySecretStore();
        var service = new ExternalConnectionRegistryService(repository, new FakeMcpClient(), secrets);
        var connection = ReadyConnection("Remote MCP");
        await repository.UpsertAsync(connection, CancellationToken.None);
        await secrets.SetAsync(ExternalConnectionNaming.SecretProviderId(connection.Id), ExternalConnectionNaming.OAuthTokenSecretName, "token-bundle", CancellationToken.None);

        await service.RemoveAsync(connection.Id, CancellationToken.None);

        Assert.Null(await repository.GetAsync(connection.Id, CancellationToken.None));
        Assert.Null(await secrets.GetAsync(ExternalConnectionNaming.SecretProviderId(connection.Id), ExternalConnectionNaming.OAuthTokenSecretName, CancellationToken.None));
        Assert.Contains((ExternalConnectionNaming.SecretProviderId(connection.Id), ExternalConnectionNaming.OAuthTokenSecretName), secrets.Deleted);
    }

    [Fact]
    public void OAuthValidationIsResponseBoundAndUefnRemainsNoAuth()
    {
        var unsafeRedirect = new McpConnectionConfiguration(McpTransportKind.StreamableHttp, "https://example.com/mcp", UseOAuth: true, OAuthRedirectUri: "https://example.com/callback");
        Assert.Throws<InvalidOperationException>(() => ExternalConnectionRegistryService.ValidateMcpConfiguration(unsafeRedirect, false));

        var uefnWithOAuth = McpConnectionConfiguration.UefnDefault with { UseOAuth = true };
        Assert.Throws<InvalidOperationException>(() => ExternalConnectionRegistryService.ValidateMcpConfiguration(uefnWithOAuth, true));
    }

    [Theory]
    [InlineData("Google", "Google Connection")]
    [InlineData("My MCP Server", "My MCP Server Connection")]
    [InlineData("School Connection", "School Connection")]
    public void DynamicConnectionNamingDoesNotDuplicateSuffix(string name, string expected) =>
        Assert.Equal(expected, ExternalConnectionNaming.PluginName(name));

    private static ActiveCapability Active(ExternalConnection connection) => new(
        ExternalConnectionNaming.CapabilityKey(connection.Id), ExternalConnectionNaming.PluginName(connection.Name), "connection", "Use connection", "connection.mcp", "haven.connections");

    private static ExternalConnection ReadyConnection(string name)
    {
        var now = DateTimeOffset.UtcNow;
        return new ExternalConnection(Guid.NewGuid(), name, "mcp.test", ExternalConnectionKind.Mcp, "custom-mcp", true, ExternalConnectionState.Ready, "Connected",
            JsonSerializer.Serialize(new McpConnectionConfiguration(McpTransportKind.StreamableHttp, "http://127.0.0.1:8765/mcp", LocalOnly: true)),
            "test-server", "1", "2026-07-28", now, now);
    }

    private static McpExternalTool Tool(string name, string description) => new(name, description, Element("{\"type\":\"object\",\"properties\":{}}"));
    private static JsonElement Element(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }

    private sealed class MemoryConnectionRepository : IExternalConnectionRepository
    {
        private readonly Dictionary<Guid, ExternalConnection> _items = [];
        public Task<IReadOnlyList<ExternalConnection>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExternalConnection>>(_items.Values.ToArray());
        public Task<ExternalConnection?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_items.GetValueOrDefault(id));
        public Task UpsertAsync(ExternalConnection connection, CancellationToken cancellationToken) { _items[connection.Id] = connection; return Task.CompletedTask; }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) { _items.Remove(id); return Task.CompletedTask; }
    }

    private sealed class MemorySecretStore : IProviderSecretStore
    {
        private readonly Dictionary<(string ProviderId, string SecretName), string> _items = [];
        public List<(string ProviderId, string SecretName)> Deleted { get; } = [];
        public Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken)
        {
            _items[(providerId, secretName)] = secret;
            return Task.CompletedTask;
        }
        public Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken) =>
            Task.FromResult(_items.GetValueOrDefault((providerId, secretName)));
        public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken)
        {
            Deleted.Add((providerId, secretName));
            _items.Remove((providerId, secretName));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMcpClient : IMcpConnectionClient
    {
        public McpServerIdentity Identity { get; set; } = new("test-server", "1", "2026-07-28", "{}");
        public IReadOnlyList<McpExternalTool> Tools { get; set; } = [];
        public int InvocationCount { get; private set; }
        public string? LastToolName { get; private set; }
        public Task<(McpServerIdentity Identity, IReadOnlyList<McpExternalTool> Tools)> DiscoverAsync(ExternalConnection connection, CancellationToken cancellationToken) => Task.FromResult((Identity, Tools));
        public Task<McpToolInvocationResult> InvokeAsync(ExternalConnection connection, string toolName, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
        {
            InvocationCount++; LastToolName = toolName;
            return Task.FromResult(new McpToolInvocationResult(true, "ok", Element("{\"ok\":true}"), "[]"));
        }
    }
}
