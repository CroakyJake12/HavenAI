using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class ModelGovernanceStoresTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "haven-model-governance-" + Guid.NewGuid().ToString("N"));

    private sealed class TempAppPaths(string dataDirectory) : IAppPaths
    {
        public string DataDirectory { get; } = dataDirectory;
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dataDirectory, true); }
        catch { /* best effort cleanup */ }
    }

    [Fact]
    public async Task FallbackOrderRoundTripsAndNormalisesDuplicates()
    {
        var settings = new VersionedAtomicSettingsStore(new TempAppPaths(_dataDirectory));
        var store = new VersionedModelFallbackOrderStore(settings);

        await store.SetOrderAsync(["Qwen 3.8", " gemini ", "Qwen 3.8", ""], CancellationToken.None);

        var order = await store.GetOrderAsync(CancellationToken.None);
        Assert.Equal(["Qwen 3.8", "gemini"], order.ToArray());
    }

    [Fact]
    public async Task FallbackOrderDefaultsToEmptyWhenUnset()
    {
        var settings = new VersionedAtomicSettingsStore(new TempAppPaths(_dataDirectory));
        var store = new VersionedModelFallbackOrderStore(settings);

        var order = await store.GetOrderAsync(CancellationToken.None);

        Assert.Empty(order);
    }

    [Fact]
    public async Task PersonalisationStorePreservesExplicitNullsForInheritance()
    {
        var settings = new VersionedAtomicSettingsStore(new TempAppPaths(_dataDirectory));
        var store = new VersionedModelPersonalisationStore(settings);

        await store.SetSharedDefaultsAsync(new ModelPersonality(Seriousness: PersonalityLevel.VeryHigh), CancellationToken.None);
        await store.SaveEntryAsync(new ModelPersonalisationEntry("openai:gpt-4o",
            Nickname: "Cloud Helper",
            Personality: new ModelPersonality(Verbosity: PersonalityLevel.Low)), CancellationToken.None);

        var reloaded = new VersionedModelPersonalisationStore(
            new VersionedAtomicSettingsStore(new TempAppPaths(_dataDirectory)));
        var shared = await reloaded.GetSharedDefaultsAsync(CancellationToken.None);
        var entries = await reloaded.GetEntriesAsync(CancellationToken.None);

        Assert.Equal(PersonalityLevel.VeryHigh, shared.Seriousness);
        var entry = Assert.Single(entries);
        Assert.Equal("openai:gpt-4o", entry.ModelKey);
        Assert.Equal("Cloud Helper", entry.Nickname);
        Assert.Equal(PersonalityLevel.Low, entry.Personality!.Verbosity);
    }

    [Fact]
    public async Task PermissionPolicyRoundTrips()
    {
        var settings = new VersionedAtomicSettingsStore(new TempAppPaths(_dataDirectory));
        var store = new VersionedModelPermissionStore(settings);
        var policy = new ModelPermissionPolicy(
        [
            ModelPermissionRule.Create(ModelPermissionTargetKind.ParameterSizeBelow, string.Empty,
                ModelPermissionScope.AcrossMesh, RestrictedModelCapability.EditFiles, RestrictedModelCapability.RunCommands)
        ]);

        await store.SavePolicyAsync(policy with
        {
            Rules =
            [
                new ModelPermissionRule(policy.Rules[0].Id, ModelPermissionTargetKind.ParameterSizeBelow, string.Empty,
                    27, ModelPermissionScope.AcrossMesh,
                    new HashSet<RestrictedModelCapability> { RestrictedModelCapability.EditFiles })
            ]
        }, CancellationToken.None);

        var loaded = await store.GetPolicyAsync(CancellationToken.None);
        var rule = Assert.Single(loaded.Rules);
        Assert.Equal(27, rule.MaxParameterBillion);
        Assert.Equal([RestrictedModelCapability.EditFiles], rule.Denied.ToArray());
    }
}
