using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>OpenAI image generation backed by Haven's configured provider endpoint and secure provider secret store.</summary>
public sealed class OpenAiImagineGenerationService(
    IHttpClientFactory httpClients,
    IProviderConfigurationStore configurations,
    IProviderSecretStore secrets) : IImagineGenerationService
{
    private const string ProviderId = "openai";
    private const string DefaultEndpoint = "https://api.openai.com/v1/";
    private const string DefaultModel = "gpt-image-2";

    public async Task<ImagineGenerationResult> GenerateAsync(ImagineGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("An image generation prompt is required.", nameof(request));

        ProviderConfiguration configuration;
        string apiKey;
        try
        {
            configuration = await ProviderHttp.RequireEnabledAsync(configurations, ProviderId, DefaultEndpoint, cancellationToken).ConfigureAwait(false);
            apiKey = await ProviderHttp.RequireSecretAsync(secrets, ProviderId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            return new ImagineGenerationResult(false, exception.Message, ProviderId, null, null, ImagineGenerationFailureKind.ConnectionRequired);
        }

        var model = configuration.Metadata.TryGetValue("imageModel", out var configuredModel) && !string.IsNullOrWhiteSpace(configuredModel)
            ? configuredModel.Trim()
            : DefaultModel;
        var size = request.Size is "1024x1536" or "1536x1024" ? request.Size : "1024x1024";
        var quality = request.Quality is "low" or "high" ? request.Quality : "medium";

        try
        {
            using var client = ProviderHttp.CreateClient(httpClients, "Haven.ModelProvider.openai", configuration);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            if (configuration.Metadata.TryGetValue("organization", out var organization) && !string.IsNullOrWhiteSpace(organization))
                client.DefaultRequestHeaders.TryAddWithoutValidation("OpenAI-Organization", organization);
            if (configuration.Metadata.TryGetValue("project", out var project) && !string.IsNullOrWhiteSpace(project))
                client.DefaultRequestHeaders.TryAddWithoutValidation("OpenAI-Project", project);

            using var response = string.IsNullOrWhiteSpace(request.ReferenceImagePath)
                ? await GenerateFromPromptAsync(client, model, request.Prompt.Trim(), size, quality, cancellationToken).ConfigureAwait(false)
                : await GenerateFromReferenceAsync(client, model, request.Prompt.Trim(), request.ReferenceImagePath!, size, quality, cancellationToken).ConfigureAwait(false);
            await ProviderHttp.EnsureSuccessAsync(response, "OpenAI", cancellationToken).ConfigureAwait(false);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var bytes = await ReadImageBytesAsync(client, document.RootElement, cancellationToken).ConfigureAwait(false);
            return bytes is { Length: > 0 }
                ? new ImagineGenerationResult(true, "Generated image with OpenAI.", ProviderId, model, bytes)
                : new ImagineGenerationResult(false, "OpenAI completed the request without returning a usable image.", ProviderId, model, null, ImagineGenerationFailureKind.ProviderError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or FormatException or IOException)
        {
            return new ImagineGenerationResult(false, exception.Message, ProviderId, model, null, ImagineGenerationFailureKind.ProviderError);
        }
    }

    private static Task<HttpResponseMessage> GenerateFromPromptAsync(HttpClient client, string model, string prompt, string size, string quality, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync("images/generations", new Dictionary<string, object?>
        {
            ["model"] = model, ["prompt"] = prompt, ["size"] = size, ["quality"] = quality, ["output_format"] = "png"
        }, ProviderHttp.Json, cancellationToken);

    private static async Task<HttpResponseMessage> GenerateFromReferenceAsync(HttpClient client, string model, string prompt, string path, string size, string quality, CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(path);
        if (!File.Exists(source)) throw new FileNotFoundException("The Imagine reference image no longer exists.", source);
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(model), "model");
        content.Add(new StringContent(prompt), "prompt");
        content.Add(new StringContent(size), "size");
        content.Add(new StringContent(quality), "quality");
        content.Add(new StringContent("png"), "output_format");
        var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var image = new StreamContent(stream);
        image.Headers.ContentType = new MediaTypeHeaderValue(ImageMimeType(source));
        content.Add(image, "image", Path.GetFileName(source));
        try { return await client.PostAsync("images/edits", content, cancellationToken).ConfigureAwait(false); }
        finally { content.Dispose(); }
    }

    private static async Task<byte[]?> ReadImageBytesAsync(HttpClient client, JsonElement root, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) return null;
        var first = data[0];
        if (first.TryGetProperty("b64_json", out var encoded) && encoded.ValueKind == JsonValueKind.String && encoded.GetString() is { Length: > 0 } base64)
            return Convert.FromBase64String(base64);
        if (first.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri))
            return await client.GetByteArrayAsync(uri, cancellationToken).ConfigureAwait(false);
        return null;
    }

    private static string ImageMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg"
    };
}
