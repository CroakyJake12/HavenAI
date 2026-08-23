/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/CloudModelProviders.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ProviderHttp, OpenAiCompatibleModelProviderBase, OpenAiModelProvider, OpenRouterModelProvider, CustomOpenAiCompatibleModelProvider. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents provider http and keeps its related state and behavior together.
/// </summary>
internal static class ProviderHttp
{
    /// <summary>
    /// Stores json locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Performs require enabled asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public static async Task<ProviderConfiguration> RequireEnabledAsync(
        IProviderConfigurationStore configurations,
        string providerId,
        string defaultEndpoint,
        CancellationToken cancellationToken)
    {
        var configuration = await configurations.GetAsync(providerId, cancellationToken).ConfigureAwait(false)
                            ?? throw new InvalidOperationException($"Provider '{providerId}' is not configured.");
        if (!configuration.IsEnabled) throw new InvalidOperationException($"{configuration.DisplayName} is disabled in Haven settings.");
        var endpoint = string.IsNullOrWhiteSpace(configuration.Endpoint) ? defaultEndpoint : configuration.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint)) throw new InvalidOperationException($"{configuration.DisplayName} requires an endpoint.");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"{configuration.DisplayName} requires an absolute HTTP or HTTPS endpoint.");
        if (endpointUri.Scheme == "http" && !endpointUri.IsLoopback)
            throw new InvalidOperationException($"{configuration.DisplayName} must use HTTPS for a non-loopback endpoint.");
        if (!endpoint.EndsWith("/", StringComparison.Ordinal)) endpoint += "/";
        return configuration with { Endpoint = endpoint };
    }

    /// <summary>
    /// Creates client with the invariants required by its callers.
    /// </summary>
    public static HttpClient CreateClient(IHttpClientFactory factory, string name, ProviderConfiguration configuration)
    {
        var client = factory.CreateClient(name);
        client.BaseAddress = new Uri(configuration.Endpoint, UriKind.Absolute);
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    /// <summary>
    /// Performs require secret asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public static async Task<string> RequireSecretAsync(IProviderSecretStore secrets, string providerId, CancellationToken cancellationToken) =>
        await secrets.GetAsync(providerId, "api-key", cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"No API key is stored for {providerId}.");

    /// <summary>
    /// Performs ensure success asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, string providerName, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (detail.Length > 16_000) detail = detail[..16_000] + "…";
        throw new HttpRequestException($"{providerName} returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
    }

    /// <summary>
    /// Performs the convert tool schema step owned by this component.
    /// </summary>
    public static object ConvertToolSchema(OllamaToolDefinition tool) => tool.InputSchema is { } raw
        ? raw
        : new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = tool.Properties,
            ["required"] = tool.Required
        };

    /// <summary>
    /// Performs the default capabilities step owned by this component.
    /// </summary>
    public static IReadOnlySet<ToolCapability> DefaultCapabilities() => new HashSet<ToolCapability>
    {
        ToolCapability.Text,
        ToolCapability.Streaming,
        ToolCapability.Tools,
        ToolCapability.StructuredOutput,
        ToolCapability.Vision,
        ToolCapability.UsageReporting
    };
}

/// <summary>
/// Represents open ai compatible model provider base and keeps its related state and behavior together.
/// </summary>
public abstract class OpenAiCompatibleModelProviderBase(
    IHttpClientFactory httpClients,
    IProviderConfigurationStore configurations,
    IProviderSecretStore secrets,
    ProviderUsageCaptureBuffer usageCapture) : IModelProvider
{
    /// <summary>
    /// Gets or updates default endpoint, the bindable or domain state represented by this property.
    /// </summary>
    protected abstract string DefaultEndpoint { get; }
    /// <summary>
    /// Reports whether open router applies to the current state.
    /// </summary>
    protected virtual bool IsOpenRouter => false;

    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public abstract string Id { get; }
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public abstract string DisplayName { get; }
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public abstract ModelProviderKind Kind { get; }
    /// <summary>
    /// Reports whether local applies to the current state.
    /// </summary>
    public bool IsLocal => false;
    /// <summary>
    /// Reports whether manage models applies to the current state.
    /// </summary>
    public bool CanManageModels => false;

    /// <summary>
    /// Performs check health asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
            using var response = await client.GetAsync("models", cancellationToken).ConfigureAwait(false);
            await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
            return new(Id, true, $"Connected to {DisplayName}.", System.Diagnostics.Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return new(Id, false, ex.Message, System.Diagnostics.Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Retrieves models async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
    {
        ProviderConfiguration configuration;
        try { configuration = await ProviderHttp.RequireEnabledAsync(configurations, Id, DefaultEndpoint, cancellationToken).ConfigureAwait(false); }
        catch (InvalidOperationException) { return []; }
        using var client = await CreateClientAsync(configuration, cancellationToken).ConfigureAwait(false);
        using var response = await client.GetAsync("models", cancellationToken).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
        var result = new List<ProviderModelDescriptor>();
        foreach (var item in data.EnumerateArray())
        {
            var name = item.TryGetProperty("id", out var id) ? id.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;
            int? context = item.TryGetProperty("context_length", out var contextElement) && contextElement.TryGetInt32(out var value) ? value : null;
            var capabilities = InferCapabilities(name, item);
            result.Add(new ProviderModelDescriptor(Id, false,
                new ModelDescriptor(name, 0, DisplayName, string.Empty, string.Empty, capabilities, DateTimeOffset.UtcNow),
                context, name));
        }
        return result;
    }

    /// <summary>
    /// Performs stream chat asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(BuildChatPayload(request, true), options: ProviderHttp.Json)
        };
        using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var payload = line[5..].Trim();
            if (payload == "[DONE]") yield break;
            if (payload.Length == 0) continue;
            using var document = JsonDocument.Parse(payload);
            CaptureUsage(document.RootElement, request.Model);
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) continue;
            var delta = choices[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } text)
                yield return text;
        }
    }

    /// <summary>
    /// Performs complete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        using var response = await client.PostAsJsonAsync("chat/completions", BuildChatPayload(request, false), ProviderHttp.Json, cancellationToken).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        CaptureUsage(document.RootElement, request.Model);
        return ReadContent(document.RootElement.GetProperty("choices")[0].GetProperty("message"));
    }

    /// <summary>
    /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        using var response = await client.PostAsJsonAsync("chat/completions", BuildToolPayload(request), ProviderHttp.Json, cancellationToken).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        CaptureUsage(document.RootElement, request.Model);
        var message = document.RootElement.GetProperty("choices")[0].GetProperty("message");
        var calls = new List<OllamaToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in toolCalls.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("function", out var function)
                    || function.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException($"{DisplayName} returned a malformed function tool call.");
                }

                var callId = item.TryGetProperty("id", out var idElement)
                    && idElement.ValueKind == JsonValueKind.String
                    ? idElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(callId))
                    throw new InvalidDataException($"{DisplayName} returned a function tool call without an identifier.");

                var name = function.TryGetProperty("name", out var nameElement)
                    && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException($"{DisplayName} returned a function tool call without a name.");

                calls.Add(new OllamaToolCall(name, ParseToolArguments(function, name), callId));
            }
        }
        return new(ReadContent(message), calls);
    }

    /// <summary>
    /// Performs the parse tool arguments step owned by this component.
    /// </summary>
    private IReadOnlyDictionary<string, JsonElement> ParseToolArguments(JsonElement function, string toolName)
    {
        if (!function.TryGetProperty("arguments", out var argumentsElement))
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var raw = argumentsElement.ValueKind switch
            {
                JsonValueKind.String => argumentsElement.GetString(),
                JsonValueKind.Object => argumentsElement.GetRawText(),
                _ => throw new InvalidDataException(
                    $"{DisplayName} returned non-object arguments for tool '{toolName}'.")
            };
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidDataException(
                    $"{DisplayName} returned empty arguments for tool '{toolName}'.");

            using var parsed = JsonDocument.Parse(raw);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(
                    $"{DisplayName} returned non-object arguments for tool '{toolName}'.");

            var arguments = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in parsed.RootElement.EnumerateObject())
            {
                if (!arguments.TryAdd(property.Name, property.Value.Clone()))
                    throw new InvalidDataException(
                        $"{DisplayName} returned duplicate argument '{property.Name}' for tool '{toolName}'.");
            }
            return arguments;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"{DisplayName} returned malformed JSON arguments for tool '{toolName}'.",
                ex);
        }
    }

    /// <summary>
    /// Creates client async with the invariants required by its callers.
    /// </summary>
    private async Task<HttpClient> CreateClientAsync(CancellationToken cancellationToken) =>
        await CreateClientAsync(await ProviderHttp.RequireEnabledAsync(configurations, Id, DefaultEndpoint, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Creates client async with the invariants required by its callers.
    /// </summary>
    private async Task<HttpClient> CreateClientAsync(ProviderConfiguration configuration, CancellationToken cancellationToken)
    {
        var client = ProviderHttp.CreateClient(httpClients, "Haven.ModelProvider." + Id, configuration);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ProviderHttp.RequireSecretAsync(secrets, Id, cancellationToken).ConfigureAwait(false));
        if (configuration.Metadata.TryGetValue("organization", out var organization) && organization.Length > 0)
            client.DefaultRequestHeaders.TryAddWithoutValidation("OpenAI-Organization", organization);
        if (configuration.Metadata.TryGetValue("project", out var project) && project.Length > 0)
            client.DefaultRequestHeaders.TryAddWithoutValidation("OpenAI-Project", project);
        if (IsOpenRouter)
        {
            if (configuration.Metadata.TryGetValue("referer", out var referer) && referer.Length > 0)
                client.DefaultRequestHeaders.TryAddWithoutValidation("HTTP-Referer", referer);
            if (configuration.Metadata.TryGetValue("title", out var title) && title.Length > 0)
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Title", title);
        }
        return client;
    }

    /// <summary>
    /// Builds chat payload from the currently available inputs.
    /// </summary>
    private object BuildChatPayload(OllamaChatRequest request, bool stream)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = BuildMessages(request.Messages, request.SystemPrompt),
            ["stream"] = stream,
            ["temperature"] = Math.Clamp(request.Options?.Temperature ?? 0.7, 0, 2)
        };
        if (stream) payload["stream_options"] = new { include_usage = true };
        return payload;
    }

    /// <summary>
    /// Builds tool payload from the currently available inputs.
    /// </summary>
    private static object BuildToolPayload(OllamaToolRequest request) => new
    {
        model = request.Model,
        messages = BuildToolMessages(request.Messages, request.SystemPrompt),
        tools = request.Tools.Select(tool => new
        {
            type = "function",
            function = new { name = tool.Name, description = tool.Description, parameters = ProviderHttp.ConvertToolSchema(tool) }
        }),
        tool_choice = "auto",
        stream = false,
        temperature = Math.Clamp(request.Options?.Temperature ?? 0.7, 0, 2)
    };

    /// <summary>
    /// Performs the capture usage step owned by this component.
    /// </summary>
    private void CaptureUsage(JsonElement root, string model)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
        var input = ReadInt64(usage, "prompt_tokens") ?? ReadInt64(usage, "input_tokens");
        var output = ReadInt64(usage, "completion_tokens") ?? ReadInt64(usage, "output_tokens");
        long? cached = null;
        long? reasoning = null;
        if (usage.TryGetProperty("prompt_tokens_details", out var promptDetails) && promptDetails.ValueKind == JsonValueKind.Object)
            cached = ReadInt64(promptDetails, "cached_tokens");
        if (usage.TryGetProperty("completion_tokens_details", out var completionDetails) && completionDetails.ValueKind == JsonValueKind.Object)
            reasoning = ReadInt64(completionDetails, "reasoning_tokens");
        if (input is null && output is null && cached is null && reasoning is null) return;
        usageCapture.Set(new ProviderUsageSnapshot(Id, model, input, output, cached, reasoning, UsageMeasurementKind.ProviderConfirmed, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Performs the read int64 step owned by this component.
    /// </summary>
    private static long? ReadInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;

    /// <summary>
    /// Builds messages from the currently available inputs.
    /// </summary>
    private static IReadOnlyList<object> BuildMessages(IReadOnlyList<OllamaMessage> messages, string? systemPrompt)
    {
        var result = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt)) result.Add(new { role = "system", content = systemPrompt });
        foreach (var message in messages)
        {
            if (message.Images is not { Count: > 0 }) { result.Add(new { role = message.Role, content = message.Content }); continue; }
            var content = new List<object> { new { type = "text", text = message.Content } };
            content.AddRange(message.Images.Select(image => (object)new { type = "image_url", image_url = new { url = "data:image/jpeg;base64," + image } }));
            result.Add(new { role = message.Role, content });
        }
        return result;
    }

    /// <summary>
    /// Builds tool messages from the currently available inputs.
    /// </summary>
    internal static IReadOnlyList<object> BuildToolMessages(IReadOnlyList<OllamaToolTurn> messages, string? systemPrompt)
    {
        var result = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt)) result.Add(new { role = "system", content = systemPrompt });

        foreach (var correlated in ProviderToolTurnCorrelation.Correlate(messages, "call_haven"))
        {
            var message = correlated.Turn;
            if (correlated.Calls.Count > 0)
            {
                result.Add(new
                {
                    role = message.Role,
                    content = message.Content,
                    tool_calls = correlated.Calls.Select(value => new
                    {
                        id = value.Id,
                        type = "function",
                        function = new
                        {
                            name = value.Call.Name,
                            arguments = JsonSerializer.Serialize(value.Call.Arguments)
                        }
                    })
                });
                continue;
            }

            if (correlated.ResultCallId is not null)
            {
                result.Add(new
                {
                    role = "tool",
                    tool_call_id = correlated.ResultCallId,
                    content = message.Content
                });
                continue;
            }

            if (message.Images is not { Count: > 0 })
            {
                result.Add(new { role = message.Role, content = message.Content });
                continue;
            }

            var content = new List<object> { new { type = "text", text = message.Content } };
            content.AddRange(message.Images.Select(image => (object)new
            {
                type = "image_url",
                image_url = new { url = "data:image/jpeg;base64," + image }
            }));
            result.Add(new { role = message.Role, content });
        }

        return result;
    }

    /// <summary>
    /// Performs the read content step owned by this component.
    /// </summary>
    private static string ReadContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content)) return string.Empty;
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? string.Empty;
        if (content.ValueKind != JsonValueKind.Array) return string.Empty;
        return string.Concat(content.EnumerateArray().Where(item => item.TryGetProperty("text", out _)).Select(item => item.GetProperty("text").GetString()));
    }

    /// <summary>
    /// Performs the infer capabilities step owned by this component.
    /// </summary>
    private static IReadOnlySet<ToolCapability> InferCapabilities(string name, JsonElement model)
    {
        var capabilities = new HashSet<ToolCapability>(ProviderHttp.DefaultCapabilities());
        if (name.Contains("embed", StringComparison.OrdinalIgnoreCase))
        {
            capabilities.Clear();
            capabilities.Add(ToolCapability.Embeddings);
        }
        if (model.TryGetProperty("architecture", out var architecture) && architecture.ValueKind == JsonValueKind.Object &&
            architecture.TryGetProperty("modality", out var modality) && modality.GetString()?.Contains("image", StringComparison.OrdinalIgnoreCase) == false)
            capabilities.Remove(ToolCapability.Vision);
        return capabilities;
    }
}

/// <summary>
/// Represents open ai model provider and keeps its related state and behavior together.
/// </summary>
public sealed class OpenAiModelProvider(
    IHttpClientFactory clients,
    IProviderConfigurationStore configurations,
    IProviderSecretStore secrets,
    ProviderUsageCaptureBuffer usageCapture)
    : OpenAiCompatibleModelProviderBase(clients, configurations, secrets, usageCapture)
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public override string Id => "openai";
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public override string DisplayName => "OpenAI";
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public override ModelProviderKind Kind => ModelProviderKind.OpenAI;
    /// <summary>
    /// Gets or updates default endpoint, the bindable or domain state represented by this property.
    /// </summary>
    protected override string DefaultEndpoint => "https://api.openai.com/v1/";
}

/// <summary>
/// Represents open router model provider and keeps its related state and behavior together.
/// </summary>
public sealed class OpenRouterModelProvider(
    IHttpClientFactory clients,
    IProviderConfigurationStore configurations,
    IProviderSecretStore secrets,
    ProviderUsageCaptureBuffer usageCapture)
    : OpenAiCompatibleModelProviderBase(clients, configurations, secrets, usageCapture)
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public override string Id => "openrouter";
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public override string DisplayName => "OpenRouter";
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public override ModelProviderKind Kind => ModelProviderKind.OpenRouter;
    /// <summary>
    /// Gets or updates default endpoint, the bindable or domain state represented by this property.
    /// </summary>
    protected override string DefaultEndpoint => "https://openrouter.ai/api/v1/";
    /// <summary>
    /// Reports whether open router applies to the current state.
    /// </summary>
    protected override bool IsOpenRouter => true;
}

/// <summary>
/// Represents custom open ai compatible model provider and keeps its related state and behavior together.
/// </summary>
public sealed class CustomOpenAiCompatibleModelProvider(
    IHttpClientFactory clients,
    IProviderConfigurationStore configurations,
    IProviderSecretStore secrets,
    ProviderUsageCaptureBuffer usageCapture)
    : OpenAiCompatibleModelProviderBase(clients, configurations, secrets, usageCapture)
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public override string Id => "openai-compatible";
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public override string DisplayName => "OpenAI-compatible";
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public override ModelProviderKind Kind => ModelProviderKind.OpenAICompatible;
    /// <summary>
    /// Gets or updates default endpoint, the bindable or domain state represented by this property.
    /// </summary>
    protected override string DefaultEndpoint => string.Empty;
}
