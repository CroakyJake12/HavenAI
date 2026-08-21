using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class ImagineProjectRepositoryTests
{
    [Fact]
    public async Task Import_save_reopen_and_bundle_preserve_managed_source_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), "haven-imagine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.png");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4, 5]);
        try
        {
            var repository = new ImagineProjectRepository(new TestPaths(root));
            var project = await repository.CreateAsync("Persistent", 900, 600, CancellationToken.None);
            var asset = await repository.ImportAssetAsync(project.Id, source, ImagineMediaKind.Image, CancellationToken.None);
            var session = new ImagineProjectSession(project);
            session.AddImportedAsset(asset);
            await repository.SaveAsync(session.Project, CancellationToken.None);

            var reopened = await repository.GetAsync(project.Id, CancellationToken.None);

            Assert.NotNull(reopened);
            var reopenedAsset = Assert.Single(reopened!.Assets);
            Assert.True(File.Exists(reopenedAsset.ManagedPath));
            Assert.NotEqual(Path.GetFullPath(source), Path.GetFullPath(reopenedAsset.ManagedPath));
            Assert.Equal([1, 2, 3, 4, 5], await File.ReadAllBytesAsync(source));

            var bundle = Path.Combine(root, "export.haven-imagine");
            Assert.Equal(bundle, await repository.ExportBundleAsync(reopened, bundle, CancellationToken.None));
            Assert.True(File.Exists(bundle));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

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
