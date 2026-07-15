using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class GeminiModelProvider(
    IHttpClientFactory httpClients,
    IProviderConfigurationStore configurations,
    IProviderSecretStore secrets) : IModelProvider
{
    private const string DefaultEndpoint = "https://generativelanguage.googleapis.com/v1beta/";

    public string Id => "gemini";
    public string DisplayName => "Google Gemini";
    public ModelProviderKind Kind => ModelProviderKind.Gemini;
    public bool IsLocal => false;
    public bool CanManageModels => false;

    public async Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
            using var response = await client.GetAsync("models?pageSize=1", cancellationToken).ConfigureAwait(false);
            await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
            return new(Id, true, "Connected to Google Gemini.", System.Diagnostics.Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return new(Id, false, ex.Message, System.Diagnostics.Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
    }

    public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
    {
        ProviderConfiguration configuration;
        try
        {
            configuration = await ProviderHttp.RequireEnabledAsync(configurations, Id, DefaultEndpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return [];
        }

        using var client = await CreateClientAsync(configuration, cancellationToken).ConfigureAwait(false);
        using var response = await client.GetAsync("models?pageSize=1000", cancellationToken).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<ProviderModelDescriptor>();
        foreach (var item in models.EnumerateArray())
        {
            var resourceName = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(resourceName))
                continue;

            var modelName = resourceName.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                ? resourceName["models/".Length..]
                : resourceName;

            var supportsGenerateContent = item.TryGetProperty("supportedGenerationMethods", out var methodsElement)
                && methodsElement.ValueKind == JsonValueKind.Array
                && methodsElement.EnumerateArray().Any(value =>
                    string.Equals(value.GetString(), "generateContent", StringComparison.OrdinalIgnoreCase));

            if (!supportsGenerateContent)
                continue;

            var displayName = item.TryGetProperty("displayName", out var displayElement) ? displayElement.GetString() : modelName;
            int? contextWindow = item.TryGetProperty("inputTokenLimit", out var contextElement)
                                 && contextElement.TryGetInt32(out var contextValue)
                ? contextValue
                : null;

            result.Add(new ProviderModelDescriptor(
                Id,
                false,
                new ModelDescriptor(modelName, 0, "Google Gemini", string.Empty, string.Empty, InferCapabilities(modelName), DateTimeOffset.UtcNow),
                contextWindow,
                displayName));
        }

        return result;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        OllamaChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{GetModelResource(request.Model)}:streamGenerateContent?alt=sse")
        {
            Content = JsonContent.Create(BuildChatPayload(request), options: ProviderHttp.Json)
        };

        using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            var json = line[5..].Trim();
            if (json.Length == 0 || json == "[DONE]")
                continue;

            using var document = JsonDocument.Parse(json);
            var text = ReadText(document.RootElement);
            if (text.Length > 0)
                yield return text;
        }
    }

    public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        using var response = await client.PostAsJsonAsync(
            $"{GetModelResource(request.Model)}:generateContent",
            BuildChatPayload(request),
            ProviderHttp.Json,
            cancellationToken).ConfigureAwait(false);

        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return ReadText(document.RootElement);
    }

    public async Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        using var response = await client.PostAsJsonAsync(
            $"{GetModelResource(request.Model)}:generateContent",
            BuildToolPayload(request),
            ProviderHttp.Json,
            cancellationToken).ConfigureAwait(false);

        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        var calls = new List<OllamaToolCall>();
        foreach (var part in EnumerateCandidateParts(document.RootElement))
        {
            if (!part.TryGetProperty("functionCall", out var functionCall) || functionCall.ValueKind != JsonValueKind.Object)
                continue;

            var name = functionCall.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var arguments = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (functionCall.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in args.EnumerateObject())
                    arguments[property.Name] = property.Value.Clone();
            }

            calls.Add(new OllamaToolCall(name, arguments));
        }

        return new OllamaToolResponse(ReadText(document.RootElement), calls);
    }

    private async Task<HttpClient> CreateClientAsync(CancellationToken cancellationToken) =>
        await CreateClientAsync(
            await ProviderHttp.RequireEnabledAsync(configurations, Id, DefaultEndpoint, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    private async Task<HttpClient> CreateClientAsync(ProviderConfiguration configuration, CancellationToken cancellationToken)
    {
        var client = ProviderHttp.CreateClient(httpClients, "Haven.ModelProvider.gemini", configuration);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "x-goog-api-key",
            await ProviderHttp.RequireSecretAsync(secrets, Id, cancellationToken).ConfigureAwait(false));
        return client;
    }

    private static object BuildChatPayload(OllamaChatRequest request)
    {
        var payload = new Dictionary<string, object>
        {
            ["contents"] = BuildChatContents(request.Messages),
            ["generationConfig"] = new
            {
                temperature = Math.Clamp(request.Options?.Temperature ?? 0.7, 0, 2),
                maxOutputTokens = Math.Clamp(request.Options?.ContextLimit / 4 ?? 4096, 256, 65536)
            }
        };

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            payload["systemInstruction"] = new { parts = new[] { new { text = request.SystemPrompt } } };

        return payload;
    }

    private static object BuildToolPayload(OllamaToolRequest request)
    {
        var payload = new Dictionary<string, object>
        {
            ["contents"] = BuildToolContents(request.Messages),
            ["generationConfig"] = new
            {
                temperature = Math.Clamp(request.Options?.Temperature ?? 0.7, 0, 2),
                maxOutputTokens = Math.Clamp(request.Options?.ContextLimit / 4 ?? 4096, 256, 65536)
            },
            ["tools"] = new[]
            {
                new
                {
                    functionDeclarations = request.Tools.Select(tool => new
                    {
                        name = tool.Name,
                        description = tool.Description,
                        parameters = ProviderHttp.ConvertToolSchema(tool)
                    })
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            payload["systemInstruction"] = new { parts = new[] { new { text = request.SystemPrompt } } };

        return payload;
    }

    private static IReadOnlyList<object> BuildChatContents(IReadOnlyList<OllamaMessage> messages)
    {
        var result = new List<object>();
        foreach (var message in messages)
        {
            var role = MapRole(message.Role);
            if (role is null)
                continue;

            var parts = new List<object>();
            if (!string.IsNullOrWhiteSpace(message.Content))
                parts.Add(new { text = message.Content });

            if (message.Images is { Count: > 0 })
            {
                parts.AddRange(message.Images.Select(image => (object)new
                {
                    inlineData = new { mimeType = "image/jpeg", data = image }
                }));
            }

            if (parts.Count > 0)
                result.Add(new { role, parts });
        }

        return result;
    }

    private static IReadOnlyList<object> BuildToolContents(IReadOnlyList<OllamaToolTurn> messages)
    {
        var result = new List<object>();
        foreach (var message in messages)
        {
            if (message.ToolCalls is { Count: > 0 })
            {
                result.Add(new
                {
                    role = "model",
                    parts = message.ToolCalls.Select(call => new
                    {
                        functionCall = new { name = call.Name, args = call.Arguments }
                    })
                });
                continue;
            }

            if (!string.IsNullOrWhiteSpace(message.ToolName))
            {
                result.Add(new
                {
                    role = "user",
                    parts = new[]
                    {
                        new
                        {
                            functionResponse = new
                            {
                                name = message.ToolName,
                                response = ParseToolResponse(message.Content)
                            }
                        }
                    }
                });
                continue;
            }

            var role = MapRole(message.Role);
            if (role is null)
                continue;

            result.Add(new { role, parts = new[] { new { text = message.Content } } });
        }

        return result;
    }

    private static object ParseToolResponse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new Dictionary<string, object?> { ["result"] = null };

        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string> { ["result"] = content };
        }
    }

    private static string? MapRole(string role) => role.ToLowerInvariant() switch
    {
        "user" => "user",
        "assistant" => "model",
        "model" => "model",
        _ => null
    };

    private static string GetModelResource(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("A Gemini model name is required.", nameof(model));

        var trimmed = model.Trim();
        if (trimmed.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["models/".Length..];

        if (trimmed.Length == 0 || trimmed.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new ArgumentException("The Gemini model name contains unsupported characters.", nameof(model));

        return "models/" + trimmed;
    }

    private static IReadOnlySet<ToolCapability> InferCapabilities(string modelName)
    {
        var capabilities = new HashSet<ToolCapability>
        {
            ToolCapability.Text,
            ToolCapability.Streaming,
            ToolCapability.UsageReporting
        };

        if (modelName.Contains("gemini", StringComparison.OrdinalIgnoreCase))
        {
            capabilities.Add(ToolCapability.Vision);
            capabilities.Add(ToolCapability.Tools);
            capabilities.Add(ToolCapability.StructuredOutput);
        }

        return capabilities;
    }

    private static IEnumerable<JsonElement> EnumerateCandidateParts(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var part in parts.EnumerateArray())
                yield return part;
        }
    }

    private static string ReadText(JsonElement root) => string.Concat(
        EnumerateCandidateParts(root)
            .Where(part => part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            .Select(part => part.GetProperty("text").GetString()));
}
