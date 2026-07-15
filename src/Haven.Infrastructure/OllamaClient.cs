using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class OllamaClient(HttpClient httpClient) : IOllamaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("api/tags", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    public async Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("api/tags", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        return payload?.Models.Select(MapModel).ToList() ?? [];
    }

    public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = JsonContent.Create(BuildPayload(request, stream: true), options: JsonOptions)
        };
        using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"Ollama returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) yield break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("error", out var error))
                throw new InvalidOperationException(error.GetString() ?? "Ollama streaming error.");
            if (document.RootElement.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
            {
                var chunk = content.GetString();
                if (!string.IsNullOrEmpty(chunk)) yield return chunk;
            }
            if (document.RootElement.TryGetProperty("done", out var done) && done.GetBoolean()) yield break;
        }
    }

    public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("api/chat", BuildPayload(request, stream: false), JsonOptions, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"Ollama returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
        }
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return document.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    public async Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("api/chat", BuildToolPayload(request), JsonOptions, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"Ollama returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (document.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException(error.GetString() ?? "Ollama tool-call error.");
        var message = document.RootElement.GetProperty("message");
        var content = message.TryGetProperty("content", out var contentElement) ? contentElement.GetString() ?? string.Empty : string.Empty;
        var calls = new List<OllamaToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in toolCalls.EnumerateArray())
            {
                if (!item.TryGetProperty("function", out var function)) continue;
                var name = function.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var arguments = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                if (function.TryGetProperty("arguments", out var argumentElement))
                {
                    if (argumentElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in argumentElement.EnumerateObject()) arguments[property.Name] = property.Value.Clone();
                    }
                    else if (argumentElement.ValueKind == JsonValueKind.String)
                    {
                        try
                        {
                            using var argumentDocument = JsonDocument.Parse(argumentElement.GetString() ?? "{}");
                            if (argumentDocument.RootElement.ValueKind == JsonValueKind.Object)
                                foreach (var property in argumentDocument.RootElement.EnumerateObject()) arguments[property.Name] = property.Value.Clone();
                        }
                        catch (JsonException) { }
                    }
                }
                calls.Add(new OllamaToolCall(name, arguments));
            }
        }
        return new OllamaToolResponse(content, calls);
    }

    public async Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("A model name is required.", nameof(model));
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/pull")
        {
            Content = JsonContent.Create(new { model = model.Trim(), stream = true }, options: JsonOptions)
        };
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"Ollama returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("error", out var error))
                throw new InvalidOperationException(error.GetString() ?? "Ollama model download failed.");
            if (document.RootElement.TryGetProperty("total", out var totalElement) &&
                document.RootElement.TryGetProperty("completed", out var completedElement) &&
                totalElement.TryGetInt64(out var total) && completedElement.TryGetInt64(out var completed) && total > 0)
                progress?.Report(Math.Clamp((double)completed / total, 0, 1));
        }
        progress?.Report(1);
    }

    public async Task DeleteModelAsync(string model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("A model name is required.", nameof(model));
        using var request = new HttpRequestMessage(HttpMethod.Delete, "api/delete")
        {
            Content = JsonContent.Create(new { model = model.Trim() }, options: JsonOptions)
        };
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"Ollama returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
        }
    }

    private static object BuildPayload(OllamaChatRequest request, bool stream)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt)) messages.Add(new { role = "system", content = request.SystemPrompt });
        messages.AddRange(request.Messages.Select(x => new { role = x.Role, content = x.Content, images = x.Images }));
        return new
        {
            model = request.Model,
            messages,
            stream,
            options = new
            {
                num_ctx = Math.Clamp(request.Options?.ContextLimit ?? 32768, 2048, 262144),
                temperature = Math.Clamp(request.Options?.Temperature ?? 0.7, 0, 2)
            },
            keep_alive = "10m"
        };
    }

    private static object BuildToolPayload(OllamaToolRequest request)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt)) messages.Add(new { role = "system", content = request.SystemPrompt });
        foreach (var message in request.Messages)
        {
            if (message.ToolCalls is { Count: > 0 })
            {
                messages.Add(new
                {
                    role = message.Role,
                    content = message.Content,
                    tool_calls = message.ToolCalls.Select(call => new
                    {
                        type = "function",
                        function = new { name = call.Name, arguments = call.Arguments }
                    })
                });
            }
            else if (!string.IsNullOrWhiteSpace(message.ToolName))
            {
                messages.Add(new { role = message.Role, content = message.Content, tool_name = message.ToolName });
            }
            else
            {
                messages.Add(new { role = message.Role, content = message.Content, images = message.Images });
            }
        }

        var tools = request.Tools.Select(tool => new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = new { type = "object", properties = tool.Properties, required = tool.Required }
            }
        });
        return new
        {
            model = request.Model,
            messages,
            tools,
            stream = false,
            options = new
            {
                num_ctx = Math.Clamp(request.Options?.ContextLimit ?? 32768, 2048, 262144),
                temperature = Math.Clamp(request.Options?.Temperature ?? 0.7, 0, 2)
            },
            keep_alive = "10m"
        };
    }

    private static ModelDescriptor MapModel(OllamaModel model)
    {
        var family = model.Details?.Family ?? string.Empty;
        var name = model.Name ?? model.Model ?? "unknown";
        var lower = (name + " " + family).ToLowerInvariant();
        var capabilities = new HashSet<ToolCapability> { ToolCapability.Text };
        if (lower.Contains("vl", StringComparison.Ordinal) || lower.Contains("vision", StringComparison.Ordinal) || lower.Contains("llava", StringComparison.Ordinal)) capabilities.Add(ToolCapability.Vision);
        if (lower.Contains("qwen", StringComparison.Ordinal) || lower.Contains("llama", StringComparison.Ordinal) || lower.Contains("mistral", StringComparison.Ordinal) || lower.Contains("gemma", StringComparison.Ordinal)) capabilities.Add(ToolCapability.Tools);
        return new ModelDescriptor(name, model.Size, family, model.Details?.ParameterSize ?? string.Empty, model.Details?.QuantizationLevel ?? string.Empty, capabilities, model.ModifiedAt);
    }

    private sealed record OllamaTagsResponse(IReadOnlyList<OllamaModel> Models);
    private sealed record OllamaModel(
        string? Name,
        string? Model,
        long Size,
        [property: JsonPropertyName("modified_at")] DateTimeOffset ModifiedAt,
        OllamaDetails? Details);

    private sealed record OllamaDetails(
        string? Family,
        [property: JsonPropertyName("parameter_size")] string? ParameterSize,
        [property: JsonPropertyName("quantization_level")] string? QuantizationLevel);
}
