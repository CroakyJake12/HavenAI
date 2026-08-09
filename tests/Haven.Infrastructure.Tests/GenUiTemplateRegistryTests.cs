using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class GenUiTemplateRegistryTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task Registry_seeds_versioned_metadata_without_claiming_foundations_are_production_complete()
    {
        var database = await CreateDatabaseAsync();
        var repository = new GenUiTemplateRepository(database);

        var templates = await repository.SearchAsync(string.Empty, CapabilityPlatform.Windows, null, 100, CancellationToken.None);

        Assert.Equal(TemplateRegistryCatalog.BuiltIns.Count, templates.Count);
        Assert.Equal(templates.Count, templates.Select(item => item.Id).Distinct().Count());
        Assert.All(templates, template =>
        {
            Assert.False(string.IsNullOrWhiteSpace(template.Version));
            Assert.False(string.IsNullOrWhiteSpace(template.CanonicalImplementation));
            Assert.NotEmpty(template.RequiredHavenUiPrimitives);
            Assert.NotEqual(GenUiTemplateMaturity.Production, template.Maturity);
        });
        Assert.All(templates.Where(template => template.IsDeterministicWithoutModel), template =>
            Assert.Empty(template.RequiredModelCapabilities));
    }

    [Fact]
    public async Task Search_is_bounded_semantic_and_app_aware()
    {
        var database = await CreateDatabaseAsync();
        var repository = new GenUiTemplateRepository(database);

        var studyGraphs = await repository.SearchAsync("plot", CapabilityPlatform.Android, "study", 3, CancellationToken.None);

        var graph = Assert.Single(studyGraphs);
        Assert.Equal("graph", graph.Key);
        Assert.Contains("study", graph.CompatibleApps);
    }

    [Fact]
    public async Task User_templates_share_the_registry_but_remain_untrusted_custom_records()
    {
        var database = await CreateDatabaseAsync();
        var repository = new GenUiTemplateRepository(database);
        var source = TemplateRegistryCatalog.BuiltIns.Single(item => item.Key == "structured-form");
        var custom = source with
        {
            Id = Guid.NewGuid(),
            Key = "my-intake-form",
            Name = "My Intake Form",
            CanonicalImplementation = "user.template.my-intake-form",
            IsBuiltIn = false,
            Maturity = GenUiTemplateMaturity.Preview,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await repository.UpsertAsync(custom, CancellationToken.None);
        var loaded = await repository.GetByKeyAsync(custom.Key, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.False(loaded.IsBuiltIn);
        Assert.Equal("user.template.my-intake-form", loaded.CanonicalImplementation);
    }

    private async Task<SqliteDatabase> CreateDatabaseAsync()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        return database;
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-genui-template-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "legacy.json");
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (IOException) { }
        }
    }
}
