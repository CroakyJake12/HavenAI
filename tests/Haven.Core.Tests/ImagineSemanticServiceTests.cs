using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ImagineSemanticServiceTests
{
    [Fact]
    public async Task Decomposition_uses_shared_vision_model_and_returns_dynamic_parented_bounds()
    {
        var image = Path.GetTempFileName();
        try
        {
            var assetId = Guid.NewGuid();
            var project = ImagineProjectSession.CreateProject("Vision") with
            {
                Assets =
                [
                    new ImagineMediaAsset(assetId, ImagineMediaKind.Image, "image.png", image, image, 1, "hash", DateTimeOffset.UtcNow)
                ]
            };
            var client = new FakeVisionClient("""
                ```json
                [
                  {"key":"subject","parentKey":null,"label":"Main subject","type":"object","x":0.1,"y":0.2,"width":0.7,"height":0.6,"confidence":0.95},
                  {"key":"detail","parentKey":"subject","label":"Detail","type":"part","x":0.2,"y":0.3,"width":0.2,"height":0.2,"confidence":0.8}
                ]
                ```
                """);
            var service = new ImagineSemanticService(client);

            var result = await service.DecomposeImageAsync(project, assetId, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal("fake-vision", result.Model);
            Assert.Equal(2, result.Components.Length);
            var subject = result.Components.Single(item => item.Key == "subject");
            var detail = result.Components.Single(item => item.Key == "detail");
            Assert.Equal(subject.Id, detail.ParentId);
            Assert.Null(subject.MaskPath);
            Assert.Single(client.LastRequest!.Messages[0].Images!);
            Assert.Equal(image, client.LastRequest.Messages[0].Images![0]);
        }
        finally
        {
            File.Delete(image);
        }
    }

    [Fact]
    public async Task No_vision_model_leaves_decomposition_explicitly_unavailable()
    {
        var image = Path.GetTempFileName();
        try
        {
            var assetId = Guid.NewGuid();
            var project = ImagineProjectSession.CreateProject("No vision") with
            {
                Assets =
                [
                    new ImagineMediaAsset(assetId, ImagineMediaKind.Image, "image.png", image, image, 1, "hash", DateTimeOffset.UtcNow)
                ]
            };
            var service = new ImagineSemanticService(new FakeVisionClient("[]", supportsVision: false));

            var result = await service.DecomposeImageAsync(project, assetId, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Empty(result.Components);
            Assert.Contains("No compatible vision model", result.Status);
        }
        finally
        {
            File.Delete(image);
        }
    }

    private sealed class FakeVisionClient(string response, bool supportsVision = true) : IProviderModelClient
    {
        public OllamaChatRequest? LastRequest { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
        {
            IReadOnlySet<ToolCapability> capabilities = supportsVision
                ? new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Vision }
                : new HashSet<ToolCapability> { ToolCapability.Text };
            return Task.FromResult<IReadOnlyList<ModelDescriptor>>(
            [
                new ModelDescriptor("fake-vision", 0, "test", "", "", capabilities, DateTimeOffset.UtcNow)
            ]);
        }

        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return await CompleteAsync(request, cancellationToken);
        }

        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }

        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(response, []));
    }
}
