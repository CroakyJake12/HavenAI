using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class ImagineGenerationRecoveryTests
{
    [Fact]
    public async Task Generated_bytes_become_durable_asset_object_and_survive_clipboard_save_reload()
    {
        var root = TempRoot();
        try
        {
            var repository = new ImagineProjectRepository(new TestPaths(root));
            var bytes = new byte[] { 1, 2, 3, 4, 5 };
            var command = new ImagineGenerationCommand(repository, new FakeGenerationService(new ImagineGenerationResult(true, "generated", "fake", "fake-image", bytes)));

            var result = await command.ExecuteAsync(new ImagineGenerationRequest("durable generated image"), "Generated", CancellationToken.None);

            Assert.True(result.Succeeded);
            var project = Assert.IsType<ImagineProject>(result.Project);
            var asset = Assert.Single(project.Assets);
            var image = Assert.Single(project.Objects);
            Assert.Equal(ImagineObjectKind.Image, image.Kind);
            Assert.Equal(asset.Id, image.AssetId);
            Assert.Equal(image.Id, project.Selection.TargetId);
            Assert.True(File.Exists(asset.ManagedPath));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), asset.Sha256);

            var reopened = Assert.IsType<ImagineProject>(await repository.GetAsync(project.Id, CancellationToken.None));
            Assert.Single(reopened.Assets);
            Assert.Single(reopened.Objects);
            var session = new ImagineProjectSession(reopened);
            session.SelectObject(reopened.Objects[0].Id);
            Assert.True(session.CopySelected());
            Assert.True(session.PasteClipboard());
            var pastedId = session.Project.Selection.TargetId!.Value;
            Assert.True(session.CommitObjectTransform(pastedId, session.Project.Objects.Single(item => item.Id == pastedId).Transform with { X = 600 }));
            Assert.Equal(2, session.Project.Objects.Length);
            Assert.Single(session.Project.Assets);
            Assert.All(session.Project.Objects, item => Assert.Equal(asset.Id, item.AssetId));
            Assert.True(session.Undo());
            Assert.True(session.CutSelected());
            Assert.True(session.PasteClipboard());
            await repository.SaveAsync(session.Project, CancellationToken.None);

            var reopenedAgain = Assert.IsType<ImagineProject>(await repository.GetAsync(project.Id, CancellationToken.None));
            Assert.Single(reopenedAgain.Assets);
            Assert.True(File.Exists(reopenedAgain.Assets[0].ManagedPath));
            Assert.Equal(asset.Sha256, reopenedAgain.Assets[0].Sha256);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Provider_failure_and_cancel_do_not_create_fake_projects()
    {
        var root = TempRoot();
        try
        {
            var repository = new ImagineProjectRepository(new TestPaths(root));
            var failed = new ImagineGenerationCommand(repository, new FakeGenerationService(new ImagineGenerationResult(false, "provider failed", "fake", "fake-image", null, ImagineGenerationFailureKind.ProviderError)));
            var failure = await failed.ExecuteAsync(new ImagineGenerationRequest("fail"), "Failure", CancellationToken.None);
            Assert.False(failure.Succeeded);
            Assert.Null(failure.Project);
            Assert.Empty(await repository.GetRecentAsync(20, CancellationToken.None));

            using var cancellation = new CancellationTokenSource();
            var cancelled = new ImagineGenerationCommand(repository, new CancellingGenerationService());
            var task = cancelled.ExecuteAsync(new ImagineGenerationRequest("cancel"), "Cancelled", cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Empty(await repository.GetRecentAsync(20, CancellationToken.None));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task Missing_openai_credential_is_reported_as_connection_required_without_http_fallback()
    {
        var configuration = new ProviderConfiguration("openai", ModelProviderKind.OpenAI, "OpenAI", "https://api.openai.com/v1/", true, false, false, new Dictionary<string, string>(), DateTimeOffset.UtcNow);
        var service = new OpenAiImagineGenerationService(new FakeHttpClientFactory(), new FakeConfigurationStore(configuration), new EmptySecretStore());

        var result = await service.GenerateAsync(new ImagineGenerationRequest("test"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ImagineGenerationFailureKind.ConnectionRequired, result.FailureKind);
        Assert.Null(result.ImageBytes);
        Assert.Contains("API key", result.Status, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeGenerationService(ImagineGenerationResult result) : IImagineGenerationService
    {
        public Task<ImagineGenerationResult> GenerateAsync(ImagineGenerationRequest request, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class CancellingGenerationService : IImagineGenerationService
    {
        public async Task<ImagineGenerationResult> GenerateAsync(ImagineGenerationRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new RejectingHandler());
        private sealed class RejectingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private sealed class FakeConfigurationStore(ProviderConfiguration configuration) : IProviderConfigurationStore
    {
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>([configuration]);
        public Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken cancellationToken) => Task.FromResult<ProviderConfiguration?>(providerId == configuration.Id ? configuration : null);
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(string providerId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptySecretStore : IProviderSecretStore
    {
        public Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task SetAsync(string providerId, string secretName, string value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "haven-imagine-generation-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root) { try { Directory.Delete(root, true); } catch { } }

    private sealed class TestPaths(string root) : IAppPaths
    {
        public string DataDirectory => Path.Combine(root, "data");
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
