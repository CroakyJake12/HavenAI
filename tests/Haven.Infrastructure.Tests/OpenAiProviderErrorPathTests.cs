/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/OpenAiProviderErrorPathTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns OpenAiProviderErrorPathTests, TestHttpClientFactory, TestHandler, TestConfigurationStore, TestSecretStore. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Net;
using System.Text;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents open ai provider error path tests and keeps its related state and behavior together.
/// </summary>
public sealed class OpenAiProviderErrorPathTests
{
    /// <summary>
    /// Performs the malformed function arguments are rejected instead of becoming empty arguments step owned by this component.
    /// </summary>
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
                            "id": "call-malformed-arguments",
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
                new OllamaToolRequest("test-model", [], [], EffortLevel.Low, null),
                CancellationToken.None));

        Assert.Contains("malformed JSON arguments", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reports whether cancelled health probe propagates cancellation is true for the current state.
    /// </summary>
    [Fact]
    public async Task CancelledHealthProbePropagatesCancellation()
    {
        var provider = CreateProvider(() => new HttpResponseMessage(HttpStatusCode.OK));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.CheckHealthAsync(cancellation.Token));
    }

    /// <summary>
    /// Creates provider with the invariants required by its callers.
    /// </summary>
    private static OpenAiModelProvider CreateProvider(Func<HttpResponseMessage> responseFactory) => new(
        new TestHttpClientFactory(responseFactory),
        new TestConfigurationStore(),
        new TestSecretStore(),
        new ProviderUsageCaptureBuffer());

    /// <summary>
    /// Represents test http client factory and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestHttpClientFactory(Func<HttpResponseMessage> responseFactory) : IHttpClientFactory
    {
        /// <summary>
        /// Creates client with the invariants required by its callers.
        /// </summary>
        public HttpClient CreateClient(string name) =>
            new(new TestHandler(responseFactory), disposeHandler: true);
    }

    /// <summary>
    /// Represents test handler and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
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
    /// Represents test configuration store and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestConfigurationStore : IProviderConfigurationStore
    {
        /// <summary>
        /// Stores configuration locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
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

        /// <summary>
        /// Retrieves all async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ProviderConfiguration>>([Configuration]);
        }

        /// <summary>
        /// Retrieves async for the current operation.
        /// </summary>
        public Task<ProviderConfiguration?> GetAsync(
            string providerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ProviderConfiguration?>(Configuration);
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
        public Task DeleteAsync(string providerId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Represents test secret store and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestSecretStore : IProviderSecretStore
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
