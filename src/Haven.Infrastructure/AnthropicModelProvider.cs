using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class AnthropicModelProvider(
    IHttpClientFactory httpClients,
    IProviderConfigurationStore configurations,
    IProviderSecretStore secrets,
    ProviderUsageCaptureBuffer usageCapture) : IModelProvider
{
    private const string DefaultEndpoint = "https://api.anthropic.com/v1/";
    public string Id => "anthropic";
    public string DisplayName => "Anthropic";
    public ModelProviderKind Kind => ModelProviderKind.Anthropic;
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
            return new(Id, true, "Connected to Anthropic.", System.Diagnostics.Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
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
            var displayName = item.TryGetProperty("display_name", out var display) ? display.GetString() : name;
            var capabilities = new HashSet<ToolCapability>
            {
                ToolCapability.Text, ToolCapability.Streaming, ToolCapability.Tools,
                ToolCapability.StructuredOutput, ToolCapability.Vision, ToolCapability.PromptCaching,
                ToolCapability.UsageReporting
            };
            result.Add(new(Id, false, new ModelDescriptor(name, 0, "Anthropic", string.Empty, string.Empty, capabilities, DateTimeOffset.UtcNow), null, displayName));
        }
        return result;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "messages")
        {
            Content = JsonContent.Create(BuildPayload(request, true), options: ProviderHttp.Json)
        };
        using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        long? inputTokens = null;
        long? outputTokens = null;
        long? cachedTokens = null;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var json = line[5..].Trim();
            if (json.Length == 0) continue;
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            if (type == "message_start" && root.TryGetProperty("message", out var startMessage) && startMessage.TryGetProperty("usage", out var startUsage))
                ReadUsage(startUsage, ref inputTokens, ref outputTokens, ref cachedTokens);
            else if (type == "message_delta" && root.TryGetProperty("usage", out var deltaUsage))
                ReadUsage(deltaUsage, ref inputTokens, ref outputTokens, ref cachedTokens);
            else if (type == "message_stop")
            {
                CaptureUsage(request.Model, inputTokens, outputTokens, cachedTokens);
                yield break;
            }

            if (type == "content_block_delta" &&
                root.TryGetProperty("delta", out var delta) && delta.TryGetProperty("text", out var text) && text.GetString() is { Length: > 0 } value)
                yield return value;
        }
        CaptureUsage(request.Model, inputTokens, outputTokens, cachedTokens);
    }

    public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        using var response = await client.PostAsJsonAsync("messages", BuildPayload(request, false), ProviderHttp.Json, cancellationToken).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        CaptureUsage(document.RootElement, request.Model);
        return ReadText(document.RootElement);
    }

    public async Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        using var response = await client.PostAsJsonAsync("messages", BuildToolPayload(request), ProviderHttp.Json, cancellationToken).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        CaptureUsage(document.RootElement, request.Model);
        var calls = new List<OllamaToolCall>();
        if (document.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (!block.TryGetProperty("type", out var type) || type.GetString() != "tool_use") continue;
                var name = block.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var arguments = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                if (block.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object)
                    foreach (var property in input.EnumerateObject()) arguments[property.Name] = property.Value.Clone();
                calls.Add(new(name, arguments));
            }
        }
        return new(ReadText(document.RootElement), calls);
    }

    private async Task<HttpClient> CreateClientAsync(CancellationToken cancellationToken) =>
        await CreateClientAsync(await ProviderHttp.RequireEnabledAsync(configurations, Id, DefaultEndpoint, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    private async Task<HttpClient> CreateClientAsync(ProviderConfiguration configuration, CancellationToken cancellationToken)
    {
        var client = ProviderHttp.CreateClient(httpClients, "Haven.ModelProvider.anthropic", configuration);
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", await ProviderHttp.RequireSecretAsync(secrets, Id, cancellationToken).ConfigureAwait(false));
        client.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-version", configuration.Metadata.TryGetValue("api-version", out var version) ? version : "2023-06-01");
        return client;
    }

    private static object BuildPayload(OllamaChatRequest request, bool stream) => new
    {
        model = request.Model,
        max_tokens = Math.Clamp(request.Options?.ContextLimit / 4 ?? 4096, 256, 32768),
        system = request.SystemPrompt,
        messages = request.Messages.Where(message => message.Role is "user" or "assistant").Select(ToMessage),
        temperature = Math.Clamp(request.Options?.Temperature ?? 0.7, 0, 1),
        stream
    };

    private static object BuildToolPayload(OllamaToolRequest request) => new
    {
        model = request.Model,
        max_tokens = Math.Clamp(request.Options?.ContextLimit / 4 ?? 4096, 256, 32768),
        system = request.SystemPrompt,
        messages = request.Messages.Where(message => message.Role is "user" or "assistant").Select(message => new { role = message.Role, content = message.Content }),
        tools = request.Tools.Select(tool => new { name = tool.Name, description = tool.Description, input_schema = ProviderHttp.ConvertToolSchema(tool) }),
        temperature = Math.Clamp(request.Options?.Temperature ?? 0.7, 0, 1)
    };

    private static object ToMessage(OllamaMessage message)
    {
        if (message.Images is not { Count: > 0 }) return new { role = message.Role, content = (object)message.Content };
        var content = new List<object>();
        content.AddRange(message.Images.Select(image => (object)new { type = "image", source = new { type = "base64", media_type = "image/jpeg", data = image } }));
        content.Add(new { type = "text", text = message.Content });
        return new { role = message.Role, content = (object)content };
    }

    private void CaptureUsage(JsonElement root, string model)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
        long? input = null;
        long? output = null;
        long? cached = null;
        ReadUsage(usage, ref input, ref output, ref cached);
        CaptureUsage(model, input, output, cached);
    }

    private void CaptureUsage(string model, long? input, long? output, long? cached)
    {
        if (input is null && output is null && cached is null) return;
        usageCapture.Set(new ProviderUsageSnapshot(
            Id, model, input, output, cached, null,
            UsageMeasurementKind.ProviderConfirmed, DateTimeOffset.UtcNow));
    }

    private static void ReadUsage(JsonElement usage, ref long? input, ref long? output, ref long? cached)
    {
        input = ReadInt64(usage, "input_tokens") ?? input;
        output = ReadInt64(usage, "output_tokens") ?? output;
        var cacheRead = ReadInt64(usage, "cache_read_input_tokens");
        var cacheCreation = ReadInt64(usage, "cache_creation_input_tokens");
        if (cacheRead is not null || cacheCreation is not null) cached = (cacheRead ?? 0) + (cacheCreation ?? 0);
    }

    private static long? ReadInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;

    private static string ReadText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return string.Empty;
        return string.Concat(content.EnumerateArray()
            .Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "text" && block.TryGetProperty("text", out _))
            .Select(block => block.GetProperty("text").GetString()));
    }
}
