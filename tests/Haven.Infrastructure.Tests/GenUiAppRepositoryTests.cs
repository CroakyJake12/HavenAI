using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class GenUiAppRepositoryTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task Generated_app_survives_repository_recreation()
    {
        var db = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(db).InitializeAsync(CancellationToken.None);
        var source = CreateApp();
        await new GenUiAppRepository(db).UpsertAsync(source, CancellationToken.None);

        var reopened = new GenUiAppRepository(new SqliteDatabase(_paths));
        var loaded = await reopened.GetAsync(source.Document.Origin.InstanceId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(source.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(source.RuntimeVersion, loaded.RuntimeVersion);
        Assert.Equal(GenUiPersistenceScope.Instance, loaded.StateSchema.Single().Persistence);
        Assert.True(loaded.Routes.Single().IsStartRoute);
        Assert.Equal("Dinner plan", loaded.Document.State["title"].GetString());

        var updated = loaded with
        {
            Document = loaded.Document with
            {
                State = new Dictionary<string, JsonElement>
                {
                    ["title"] = JsonSerializer.SerializeToElement("Dinner plan v2")
                },
                UpdatedAt = loaded.Document.UpdatedAt.AddMinutes(1)
            }
        };
        await reopened.UpsertAsync(updated, CancellationToken.None);
        var restored = await new GenUiAppRepository(new SqliteDatabase(_paths))
            .GetAsync(source.Document.Origin.InstanceId, CancellationToken.None);
        Assert.Equal("Dinner plan v2", restored!.Document.State["title"].GetString());
    }

    private static GenUiAppDefinition CreateApp()
    {
        var origin = new GenUiOrigin(Guid.NewGuid(), "genui", null, Guid.NewGuid());
        var root = new GenUiComponent("root", "HavenWorkspace", new Dictionary<string, JsonElement>(), [], []);
        var document = new GenUiDocument(Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin,
            "Meal planner", "genui", root,
            new Dictionary<string, JsonElement> { ["title"] = JsonSerializer.SerializeToElement("Dinner plan") },
            DateTimeOffset.UtcNow);
        return new GenUiAppDefinition("meal-planner", GenUiSemanticValidator.CurrentSchemaVersion, document,
            [new("title", GenUiValueType.String, GenUiPersistenceScope.Instance, true, "Dinner plan")], [],
            [new("root", "title", "title", GenUiBindingMode.OneWay)],
            [new("home", "root", GenUiNavigationKind.Root, null, null, true)], "haven-genui-runtime/1");
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-genui-app-tests-" + Guid.NewGuid().ToString("N"));
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
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
