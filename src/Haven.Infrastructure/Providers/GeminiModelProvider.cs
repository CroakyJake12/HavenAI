/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/GeminiModelProvider.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns GeminiModelProvider. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents gemini model provider and keeps its related state and behavior together.
/// </summary>
public sealed class GeminiModelProvider(
    IHttpClientFactory httpClients,
    IProviderConfigurationStore configurations,
    IProviderSecretStore secrets,
    ProviderUsageCaptureBuffer usageCapture) : IModelProvider
{
    /// <summary>
    /// Stores default endpoint locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const string DefaultEndpoint = "https://generativelanguage.googleapis.com/v1beta/";

    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public string Id => "gemini";
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public string DisplayName => "Google Gemini";
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public ModelProviderKind Kind => ModelProviderKind.Gemini;
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
            using var response = await client.GetAsync("models?pageSize=1", cancellationToken).ConfigureAwait(false);
            await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
            return new(Id, true, "Connected to Google Gemini.", System.Diagnostics.Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
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
            if (string.IsNullOrWhiteSpace(resourceName)) continue;
            var modelName = resourceName.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                ? resourceName["models/".Length..]
                : resourceName;
            var supportsGenerateContent = item.TryGetProperty("supportedGenerationMethods", out var methodsElement)
                && methodsElement.ValueKind == JsonValueKind.Array
                && methodsElement.EnumerateArray().Any(value => string.Equals(value.GetString(), "generateContent", StringComparison.OrdinalIgnoreCase));
            if (!supportsGenerateContent) continue;
            var displayName = item.TryGetProperty("displayName", out var displayElement) ? displayElement.GetString() : modelName;
            int? contextWindow = item.TryGetProperty("inputTokenLimit", out var contextElement) && contextElement.TryGetInt32(out var contextValue)
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

    /// <summary>
    /// Performs stream chat asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async IAsyncEnumerable<string> StreamChatAsync(
        OllamaChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{GetModelResource(request.Model)}:streamGenerateContent?alt=sse")
        {
            Content = JsonContent.Create(BuildChatPayload(request), options: ProviderHttp.Json)
        };
        using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        ProviderUsageSnapshot? lastUsage = null;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var json = line[5..].Trim();
            if (json.Length == 0 || json == "[DONE]") continue;
            using var document = JsonDocument.Parse(json);
            lastUsage = ReadUsage(document.RootElement, request.Model) ?? lastUsage;
            var text = ReadText(document.RootElement);
            if (text.Length > 0) yield return text;
        }
        if (lastUsage is not null) usageCapture.Set(lastUsage);
    }

    /// <summary>
    /// Performs complete asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
        if (ReadUsage(document.RootElement, request.Model) is { } usage) usageCapture.Set(usage);
        return ReadText(document.RootElement);
    }

    /// <summary>
    /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
        if (ReadUsage(document.RootElement, request.Model) is { } usage) usageCapture.Set(usage);

        var calls = new List<OllamaToolCall>();
        foreach (var part in EnumerateCandidateParts(document.RootElement))
        {
            if (!part.TryGetProperty("functionCall", out var functionCall) || functionCall.ValueKind != JsonValueKind.Object) continue;

            string? callId = null;
            if (functionCall.TryGetProperty("id", out var idElement))
            {
                if (idElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(idElement.GetString()))
                    throw new InvalidDataException("Gemini returned a functionCall with an invalid identifier.");
                callId = idElement.GetString();
            }

            var name = functionCall.TryGetProperty("name", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("Gemini returned a functionCall without a name.");

            var arguments = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (functionCall.TryGetProperty("args", out var args))
            {
                if (args.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"Gemini returned non-object args for function '{name}'.");
                foreach (var property in args.EnumerateObject())
                {
                    if (!arguments.TryAdd(property.Name, property.Value.Clone()))
                        throw new InvalidDataException(
                            $"Gemini returned duplicate arg '{property.Name}' for function '{name}'.");
                }
            }
            calls.Add(new OllamaToolCall(name, arguments, callId));
        }
        return new OllamaToolResponse(ReadText(document.RootElement), calls);
    }

    /// <summary>
    /// Creates client async with the invariants required by its callers.
    /// </summary>
    private async Task<HttpClient> CreateClientAsync(CancellationToken cancellationToken) =>
        await CreateClientAsync(
            await ProviderHttp.RequireEnabledAsync(configurations, Id, DefaultEndpoint, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Creates client async with the invariants required by its callers.
    /// </summary>
    private async Task<HttpClient> CreateClientAsync(ProviderConfiguration configuration, CancellationToken cancellationToken)
    {
        var client = ProviderHttp.CreateClient(httpClients, "Haven.ModelProvider.gemini", configuration);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "x-goog-api-key",
            await ProviderHttp.RequireSecretAsync(secrets, Id, cancellationToken).ConfigureAwait(false));
        return client;
    }

    /// <summary>
    /// Builds chat payload from the currently available inputs.
    /// </summary>
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

    /// <summary>
    /// Builds tool payload from the currently available inputs.
    /// </summary>
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

    /// <summary>
    /// Builds chat contents from the currently available inputs.
    /// </summary>
    private static IReadOnlyList<object> BuildChatContents(IReadOnlyList<OllamaMessage> messages)
    {
        var result = new List<object>();
        foreach (var message in messages)
        {
            var role = MapRole(message.Role);
            if (role is null) continue;
            var parts = new List<object>();
            if (!string.IsNullOrWhiteSpace(message.Content)) parts.Add(new { text = message.Content });
            if (message.Images is { Count: > 0 })
                parts.AddRange(message.Images.Select(image => (object)new { inlineData = new { mimeType = "image/jpeg", data = image } }));
            if (parts.Count > 0) result.Add(new { role, parts });
        }
        return result;
    }

    /// <summary>
    /// Builds tool contents from the currently available inputs.
    /// </summary>
    internal static IReadOnlyList<object> BuildToolContents(IReadOnlyList<OllamaToolTurn> messages)
    {
        var correlated = ProviderToolTurnCorrelation.Correlate(messages, "gemini_haven");
        var result = new List<object>(correlated.Count);

        for (var index = 0; index < correlated.Count; index++)
        {
            var current = correlated[index];
            var message = current.Turn;

            if (current.Calls.Count > 0)
            {
                var parts = new List<object>();
                if (!string.IsNullOrWhiteSpace(message.Content))
                    parts.Add(new { text = message.Content });
                parts.AddRange(current.Calls.Select(value => (object)new
                {
                    functionCall = new
                    {
                        id = value.Id,
                        name = value.Call.Name,
                        args = value.Call.Arguments
                    }
                }));
                result.Add(new { role = "model", parts });
                continue;
            }

            if (current.ResultCallId is not null)
            {
                var parts = new List<object>();
                while (index < correlated.Count && correlated[index].ResultCallId is not null)
                {
                    var toolResult = correlated[index];
                    parts.Add(new
                    {
                        functionResponse = new
                        {
                            id = toolResult.ResultCallId,
                            name = toolResult.Turn.ToolName,
                            response = ParseToolResponse(toolResult.Turn.Content)
                        }
                    });
                    index++;
                }
                index--;
                result.Add(new { role = "user", parts });
                continue;
            }

            var role = MapRole(message.Role);
            if (role is null) continue;
            var normalParts = new List<object>();
            if (!string.IsNullOrWhiteSpace(message.Content))
                normalParts.Add(new { text = message.Content });
            if (message.Images is { Count: > 0 })
            {
                normalParts.AddRange(message.Images.Select(image => (object)new
                {
                    inlineData = new { mimeType = "image/jpeg", data = image }
                }));
            }
            if (normalParts.Count > 0)
                result.Add(new { role, parts = normalParts });
        }

        return result;
    }

    /// <summary>
    /// Performs the parse tool response step owned by this component.
    /// </summary>
    private static object ParseToolResponse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return new Dictionary<string, object?> { ["result"] = null };
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

    /// <summary>
    /// Performs the map role step owned by this component.
    /// </summary>
    private static string? MapRole(string role) => role.ToLowerInvariant() switch
    {
        "user" => "user",
        "assistant" => "model",
        "model" => "model",
        _ => null
    };

    /// <summary>
    /// Retrieves model resource for the current operation.
    /// </summary>
    private static string GetModelResource(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("A Gemini model name is required.", nameof(model));
        var trimmed = model.Trim();
        if (trimmed.StartsWith("models/", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed["models/".Length..];
        if (trimmed.Length == 0 || trimmed.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new ArgumentException("The Gemini model name contains unsupported characters.", nameof(model));
        return "models/" + trimmed;
    }

    /// <summary>
    /// Performs the infer capabilities step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the read usage step owned by this component.
    /// </summary>
    private static ProviderUsageSnapshot? ReadUsage(JsonElement root, string model)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage) || usage.ValueKind != JsonValueKind.Object) return null;
        var input = ReadInt64(usage, "promptTokenCount");
        var output = ReadInt64(usage, "candidatesTokenCount");
        var cached = ReadInt64(usage, "cachedContentTokenCount");
        var reasoning = ReadInt64(usage, "thoughtsTokenCount");
        if (input is null && output is null && cached is null && reasoning is null) return null;
        return new ProviderUsageSnapshot(
            "gemini", model, input, output, cached, reasoning,
            UsageMeasurementKind.ProviderConfirmed, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Performs the read int64 step owned by this component.
    /// </summary>
    private static long? ReadInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;

    /// <summary>
    /// Performs the enumerate candidate parts step owned by this component.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateCandidateParts(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array) yield break;
        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array) continue;
            foreach (var part in parts.EnumerateArray()) yield return part;
        }
    }

    /// <summary>
    /// Performs the read text step owned by this component.
    /// </summary>
    private static string ReadText(JsonElement root) => string.Concat(
        EnumerateCandidateParts(root)
            .Where(part => part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            .Select(part => part.GetProperty("text").GetString()));
}
