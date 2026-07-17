using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

internal static class ProviderHttp
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
        if (!endpoint.EndsWith("/", StringComparison.Ordinal)) endpoint += "/";
        return configuration with { Endpoint = endpoint };
    }

    public static HttpClient CreateClient(IHttpClientFactory factory, string name, ProviderConfiguration configuration)
    {
        var client = factory.CreateClient(name);
        client.BaseAddress = new Uri(configuration.Endpoint, UriKind.Absolute);
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    public static async Task<string> RequireSecretAsync(IProviderSecretStore secrets, string providerId, CancellationToken cancellationToken) =>
        await secrets.GetAsync(providerId, "api-key", cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"No API key is stored for {providerId}.");

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, string providerName, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (detail.Length > 16_000) detail = detail[..16_000] + "…";
        throw new HttpRequestException($"{providerName} returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
    }

    public static Dictionary<string, object> ConvertToolSchema(OllamaToolDefinition tool) => new()
    {
        ["type"] = "object",
        ["properties"] = tool.Properties,
        ["required"] = tool.Required
    };

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

public abstract class OpenAiCompatibleModelProviderBase(
    IHttpClientFactory httpClients,
    IProviderConfigurationStore configurations,
    IProviderSecretStore secrets,
    ProviderUsageCaptureBuffer usageCapture) : IModelProvider
{
    protected abstract string DefaultEndpoint { get; }
    protected virtual bool IsOpenRouter => false;

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract ModelProviderKind Kind { get; }
    public bool IsLocal => false;
    public bool CanManageModels => false;

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

    public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        using var response = await client.PostAsJsonAsync("chat/completions", BuildChatPayload(request, false), ProviderHttp.Json, cancellationToken).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        CaptureUsage(document.RootElement, request.Model);
        return ReadContent(document.RootElement.GetProperty("choices")[0].GetProperty("message"));
    }

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

                var name = function.TryGetProperty("name", out var nameElement)
                    && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException($"{DisplayName} returned a function tool call without a name.");

                calls.Add(new OllamaToolCall(name, ParseToolArguments(function, name)));
            }
        }
        return new(ReadContent(message), calls);
    }

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

    private async Task<HttpClient> CreateClientAsync(CancellationToken cancellationToken) =>
        await CreateClientAsync(await ProviderHttp.RequireEnabledAsync(configurations, Id, DefaultEndpoint, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

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

    private static long? ReadInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;

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

    private static string ReadContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content)) return string.Empty;
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? string.Empty;
        if (content.ValueKind != JsonValueKind.Array) return string.Empty;
        return string.Concat(content.EnumerateArray().Where(item => item.TryGetProperty("text", out _)).Select(item => item.GetProperty("text").GetString()));
    }

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

public sealed class OpenAiModelProvider(
    IHttpClientFactory clients,
    IProviderConfigurationStore configurations,
    IProviderSecretStore secrets,
    ProviderUsageCaptureBuffer usageCapture)
    : OpenAiCompatibleModelProviderBase(clients, configurations, secrets, usageCapture)
{
    public override string Id => "openai";
    public override string DisplayName => "OpenAI";
    public override ModelProviderKind Kind => ModelProviderKind.OpenAI;
    protected override string DefaultEndpoint => "https://api.openai.com/v1/";
}

public sealed class OpenRouterModelProvider(
    IHttpClientFactory clients,
    IProviderConfigurationStore configurations,
    IProviderSecretStore secrets,
    ProviderUsageCaptureBuffer usageCapture)
    : OpenAiCompatibleModelProviderBase(clients, configurations, secrets, usageCapture)
{
    public override string Id => "openrouter";
    public override string DisplayName => "OpenRouter";
    public override ModelProviderKind Kind => ModelProviderKind.OpenRouter;
    protected override string DefaultEndpoint => "https://openrouter.ai/api/v1/";
    protected override bool IsOpenRouter => true;
}

public sealed class CustomOpenAiCompatibleModelProvider(
    IHttpClientFactory clients,
    IProviderConfigurationStore configurations,
    IProviderSecretStore secrets,
    ProviderUsageCaptureBuffer usageCapture)
    : OpenAiCompatibleModelProviderBase(clients, configurations, secrets, usageCapture)
{
    public override string Id => "openai-compatible";
    public override string DisplayName => "OpenAI-compatible";
    public override ModelProviderKind Kind => ModelProviderKind.OpenAICompatible;
    protected override string DefaultEndpoint => string.Empty;
}
