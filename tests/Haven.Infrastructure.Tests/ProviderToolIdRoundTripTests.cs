/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/ProviderToolIdRoundTripTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ProviderToolIdRoundTripTests, StaticHttpClientFactory, StaticHandler, ConfigurationStore, SecretStore. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Net;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents provider tool id round trip tests and keeps its related state and behavior together.
/// </summary>
public sealed class ProviderToolIdRoundTripTests
{
    /// <summary>
    /// Performs the open ai issued id is reused by next tool result request step owned by this component.
    /// </summary>
    [Fact]
    public async Task OpenAiIssuedIdIsReusedByNextToolResultRequest()
    {
        const string issuedId = "call_real_openai_123";
        var provider = new OpenAiModelProvider(
            Factory(
                """
                {
                  "choices": [
                    {
                      "message": {
                        "content": "",
                        "tool_calls": [
                          {
                            "id": "call_real_openai_123",
                            "type": "function",
                            "function": {
                              "name": "read_file",
                              "arguments": "{\"path\":\"a.txt\"}"
                            }
                          }
                        ]
                      }
                    }
                  ]
                }
                """),
            new ConfigurationStore("openai", ModelProviderKind.OpenAI),
            new SecretStore(),
            new ProviderUsageCaptureBuffer());

        var response = await provider.ChatWithToolsAsync(Request(), CancellationToken.None);
        var call = Assert.Single(response.ToolCalls);
        Assert.Equal(issuedId, call.Id);

        var messages = OpenAiCompatibleModelProviderBase.BuildToolMessages(History(call), null);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(messages, ProviderHttp.Json));
        Assert.Equal(issuedId, document.RootElement[0].GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal(issuedId, document.RootElement[1].GetProperty("tool_call_id").GetString());
    }

    /// <summary>
    /// Performs the anthropic issued id is reused by next tool result request step owned by this component.
    /// </summary>
    [Fact]
    public async Task AnthropicIssuedIdIsReusedByNextToolResultRequest()
    {
        const string issuedId = "toolu_real_anthropic_456";
        var provider = new AnthropicModelProvider(
            Factory(
                """
                {
                  "content": [
                    {
                      "type": "tool_use",
                      "id": "toolu_real_anthropic_456",
                      "name": "read_file",
                      "input": { "path": "a.txt" }
                    }
                  ]
                }
                """),
            new ConfigurationStore("anthropic", ModelProviderKind.Anthropic),
            new SecretStore(),
            new ProviderUsageCaptureBuffer());

        var response = await provider.ChatWithToolsAsync(Request(), CancellationToken.None);
        var call = Assert.Single(response.ToolCalls);
        Assert.Equal(issuedId, call.Id);

        var messages = AnthropicModelProvider.BuildToolMessages(History(call));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(messages, ProviderHttp.Json));
        Assert.Equal(issuedId, document.RootElement[0].GetProperty("content")[0].GetProperty("id").GetString());
        Assert.Equal(issuedId, document.RootElement[1].GetProperty("content")[0].GetProperty("tool_use_id").GetString());
    }

    /// <summary>
    /// Performs the gemini issued id is reused by next function response request step owned by this component.
    /// </summary>
    [Fact]
    public async Task GeminiIssuedIdIsReusedByNextFunctionResponseRequest()
    {
        const string issuedId = "gemini_real_789";
        var provider = new GeminiModelProvider(
            Factory(
                """
                {
                  "candidates": [
                    {
                      "content": {
                        "parts": [
                          {
                            "functionCall": {
                              "id": "gemini_real_789",
                              "name": "read_file",
                              "args": { "path": "a.txt" }
                            }
                          }
                        ]
                      }
                    }
                  ]
                }
                """),
            new ConfigurationStore("gemini", ModelProviderKind.Gemini),
            new SecretStore(),
            new ProviderUsageCaptureBuffer());

        var response = await provider.ChatWithToolsAsync(Request(), CancellationToken.None);
        var call = Assert.Single(response.ToolCalls);
        Assert.Equal(issuedId, call.Id);

        var contents = GeminiModelProvider.BuildToolContents(History(call));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(contents, ProviderHttp.Json));
        Assert.Equal(issuedId, document.RootElement[0].GetProperty("parts")[0].GetProperty("functionCall").GetProperty("id").GetString());
        Assert.Equal(issuedId, document.RootElement[1].GetProperty("parts")[0].GetProperty("functionResponse").GetProperty("id").GetString());
    }

    /// <summary>
    /// Performs the request step owned by this component.
    /// </summary>
    private static OllamaToolRequest Request() => new(
        "test-model",
        [],
        [new OllamaToolDefinition(
            "read_file",
            "Read a file.",
            new Dictionary<string, object>
            {
                ["path"] = new Dictionary<string, object> { ["type"] = "string" }
            },
            ["path"])],
        EffortLevel.Medium);

    /// <summary>
    /// Performs the history step owned by this component.
    /// </summary>
    private static IReadOnlyList<OllamaToolTurn> History(OllamaToolCall call) =>
    [
        new OllamaToolTurn("assistant", string.Empty, [call]),
        new OllamaToolTurn("tool", "file contents", ToolName: call.Name)
    ];

    /// <summary>
    /// Performs the factory step owned by this component.
    /// </summary>
    private static IHttpClientFactory Factory(string json) =>
        new StaticHttpClientFactory(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    /// <summary>
    /// Represents static http client factory and keeps its related state and behavior together.
    /// </summary>
    private sealed class StaticHttpClientFactory(Func<HttpResponseMessage> responseFactory) : IHttpClientFactory
    {
        /// <summary>
        /// Creates client with the invariants required by its callers.
        /// </summary>
        public HttpClient CreateClient(string name) =>
            new(new StaticHandler(responseFactory), disposeHandler: true);
    }

    /// <summary>
    /// Represents static handler and keeps its related state and behavior together.
    /// </summary>
    private sealed class StaticHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        /// <summary>
        /// Performs send asynchronously so I/O does not block the caller's thread.
        /// </summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responseFactory());
        }
    }

    /// <summary>
    /// Represents configuration store and keeps its related state and behavior together.
    /// </summary>
    private sealed class ConfigurationStore(
        string providerId,
        ModelProviderKind kind) : IProviderConfigurationStore
    {
        /// <summary>
        /// Stores configuration locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly ProviderConfiguration _configuration = new(
            providerId,
            kind,
            providerId,
            "https://provider.test/v1/",
            true,
            false,
            false,
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);

        /// <summary>
        /// Retrieves all async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ProviderConfiguration>>([_configuration]);
        }

        /// <summary>
        /// Retrieves async for the current operation.
        /// </summary>
        public Task<ProviderConfiguration?> GetAsync(
            string requestedProviderId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ProviderConfiguration?>(
                requestedProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase)
                    ? _configuration
                    : null);
        }

        /// <summary>
        /// Performs upsert asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task UpsertAsync(
            ProviderConfiguration configuration,
            CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Performs delete asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteAsync(
            string requestedProviderId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Represents secret store and keeps its related state and behavior together.
    /// </summary>
    private sealed class SecretStore : IProviderSecretStore
    {
        /// <summary>
        /// Performs set asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task SetAsync(
            string providerId,
            string secretName,
            string secret,
            CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Retrieves async for the current operation.
        /// </summary>
        public Task<string?> GetAsync(
            string providerId,
            string secretName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>("test-key");
        }

        /// <summary>
        /// Performs delete asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteAsync(
            string providerId,
            string secretName,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
