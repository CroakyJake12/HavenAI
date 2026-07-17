using System.Net;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class ProviderToolIdRoundTripTests
{
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

    private static IReadOnlyList<OllamaToolTurn> History(OllamaToolCall call) =>
    [
        new OllamaToolTurn("assistant", string.Empty, [call]),
        new OllamaToolTurn("tool", "file contents", ToolName: call.Name)
    ];

    private static IHttpClientFactory Factory(string json) =>
        new StaticHttpClientFactory(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    private sealed class StaticHttpClientFactory(Func<HttpResponseMessage> responseFactory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new StaticHandler(responseFactory), disposeHandler: true);
    }

    private sealed class StaticHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responseFactory());
        }
    }

    private sealed class ConfigurationStore(
        string providerId,
        ModelProviderKind kind) : IProviderConfigurationStore
    {
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

        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ProviderConfiguration>>([_configuration]);
        }

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

        public Task UpsertAsync(
            ProviderConfiguration configuration,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(
            string requestedProviderId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SecretStore : IProviderSecretStore
    {
        public Task SetAsync(
            string providerId,
            string secretName,
            string secret,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> GetAsync(
            string providerId,
            string secretName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>("test-key");
        }

        public Task DeleteAsync(
            string providerId,
            string secretName,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
