using System.Net;
using System.Text;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class OpenAiProviderErrorPathTests
{
    [Fact]
    public async Task MalformedFunctionArgumentsAreRejectedInsteadOfBecomingEmptyArguments()
    {
        var provider = CreateProvider(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "choices": [
                    {
                      "message": {
                        "content": "",
                        "tool_calls": [
                          {
                            "type": "function",
                            "function": {
                              "name": "read_file",
                              "arguments": "{ malformed"
                            }
                          }
                        ]
                      }
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.ChatWithToolsAsync(
                new OllamaToolRequest("test-model", null, [], [], null),
                CancellationToken.None));

        Assert.Contains("malformed JSON arguments", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelledHealthProbePropagatesCancellation()
    {
        var provider = CreateProvider(() => new HttpResponseMessage(HttpStatusCode.OK));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.CheckHealthAsync(cancellation.Token));
    }

    private static OpenAiModelProvider CreateProvider(Func<HttpResponseMessage> responseFactory) => new(
        new TestHttpClientFactory(responseFactory),
        new TestConfigurationStore(),
        new TestSecretStore(),
        new ProviderUsageCaptureBuffer());

    private sealed class TestHttpClientFactory(Func<HttpResponseMessage> responseFactory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new TestHandler(responseFactory), disposeHandler: true);
    }

    private sealed class TestHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responseFactory());
        }
    }

    private sealed class TestConfigurationStore : IProviderConfigurationStore
    {
        private static readonly ProviderConfiguration Configuration = new(
            "openai",
            ModelProviderKind.OpenAI,
            "OpenAI",
            "https://api.test/v1/",
            true,
            false,
            false,
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);

        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ProviderConfiguration>>([Configuration]);
        }

        public Task<ProviderConfiguration?> GetAsync(
            string providerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ProviderConfiguration?>(Configuration);
        }

        public Task UpsertAsync(
            ProviderConfiguration configuration,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(string providerId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestSecretStore : IProviderSecretStore
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
