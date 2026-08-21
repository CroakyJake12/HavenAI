using Haven.Application;

namespace Haven.Core.Tests;

public sealed class ProjectorSessionRecoveryTests
{
    [Fact]
    public async Task PersistentExperienceRestoresAcrossCoordinatorRecreation()
    {
        var settings = new InMemorySettingsStore();
        var experience = Experience("desktop", ProjectorExperiencePersistence.Persistent);
        await PersistActiveSessionAsync(settings, experience, Display("display:7", "monitor-a", ProjectorDisplayTrust.Private));

        var registry = new ProjectorDisplayRegistry();
        var replacement = Display("display:11", "monitor-a", ProjectorDisplayTrust.Private);
        registry.Upsert(replacement);
        using var coordinator = new ProjectorSessionCoordinator(registry);
        using var recovery = new ProjectorSessionRecoveryService(coordinator, registry, Catalog(experience), settings);

        var restored = await recovery.RecoverAsync(replacement, CancellationToken.None);

        Assert.Equal(ProjectorSessionState.Active, restored?.State);
        Assert.Equal("desktop", restored?.CurrentExperienceId);
        Assert.Equal(replacement.RuntimeId, restored?.TargetDisplay.RuntimeId);
    }

    [Fact]
    public async Task RestoreFallsBackToGalleryWhenDisplayTrustDrops()
    {
        var settings = new InMemorySettingsStore();
        var experience = Experience("private-work", ProjectorExperiencePersistence.Persistent, ProjectorContentSensitivity.Private);
        await PersistActiveSessionAsync(settings, experience, Display("display:2", "panel-a", ProjectorDisplayTrust.Private));

        var registry = new ProjectorDisplayRegistry();
        var replacement = Display("display:8", "panel-a", ProjectorDisplayTrust.Public);
        registry.Upsert(replacement);
        using var coordinator = new ProjectorSessionCoordinator(registry);
        using var recovery = new ProjectorSessionRecoveryService(coordinator, registry, Catalog(experience), settings);

        var restored = await recovery.RecoverAsync(replacement, CancellationToken.None);

        Assert.Equal(ProjectorSessionState.Gallery, restored?.State);
        Assert.Null(restored?.CurrentExperienceId);
    }

    [Fact]
    public async Task RestoreFallsBackToGalleryWhenRequiredCapabilityDisappears()
    {
        var settings = new InMemorySettingsStore();
        var experience = Experience("controller-space", ProjectorExperiencePersistence.Persistent, ProjectorContentSensitivity.Private, ProjectorCapability.RenderHavenSurface, ProjectorCapability.ControllerInput);
        await PersistActiveSessionAsync(settings, experience, Display("display:3", "panel-controller", ProjectorDisplayTrust.Private, ProjectorCapabilityState.Available));

        var registry = new ProjectorDisplayRegistry();
        var replacement = Display("display:9", "panel-controller", ProjectorDisplayTrust.Private, ProjectorCapabilityState.Unavailable);
        registry.Upsert(replacement);
        using var coordinator = new ProjectorSessionCoordinator(registry);
        using var recovery = new ProjectorSessionRecoveryService(coordinator, registry, Catalog(experience), settings);

        var restored = await recovery.RecoverAsync(replacement, CancellationToken.None);

        Assert.Equal(ProjectorSessionState.Gallery, restored?.State);
        Assert.Null(restored?.CurrentExperienceId);
    }

    [Fact]
    public async Task SessionOnlyExperienceDoesNotAutoRestoreAfterCoordinatorRecreation()
    {
        var settings = new InMemorySettingsStore();
        var experience = Experience("android-app:test", ProjectorExperiencePersistence.Session);
        await PersistActiveSessionAsync(settings, experience, Display("display:4", "panel-app", ProjectorDisplayTrust.Private));

        var registry = new ProjectorDisplayRegistry();
        var replacement = Display("display:10", "panel-app", ProjectorDisplayTrust.Private);
        registry.Upsert(replacement);
        using var coordinator = new ProjectorSessionCoordinator(registry);
        using var recovery = new ProjectorSessionRecoveryService(coordinator, registry, Catalog(experience), settings);

        var restored = await recovery.RecoverAsync(replacement, CancellationToken.None);

        Assert.Equal(ProjectorSessionState.Gallery, restored?.State);
        Assert.Null(restored?.CurrentExperienceId);
    }

    [Fact]
    public async Task InMemoryReconnectRevalidatesExperienceAgainstReplacementDisplay()
    {
        var settings = new InMemorySettingsStore();
        var experience = Experience("private-work", ProjectorExperiencePersistence.Persistent, ProjectorContentSensitivity.Private);
        var registry = new ProjectorDisplayRegistry();
        var first = Display("display:5", "monitor-private", ProjectorDisplayTrust.Private);
        registry.Upsert(first);
        using var coordinator = new ProjectorSessionCoordinator(registry);
        using var recovery = new ProjectorSessionRecoveryService(coordinator, registry, Catalog(experience), settings);
        coordinator.Start(first);
        coordinator.Activate(experience);
        registry.Remove(first.RuntimeId);

        var replacement = Display("display:12", "monitor-private", ProjectorDisplayTrust.Public);
        registry.Upsert(replacement);
        var restored = await recovery.RecoverAsync(replacement, CancellationToken.None);

        Assert.Equal(ProjectorSessionState.Gallery, restored?.State);
        Assert.Null(restored?.CurrentExperienceId);
    }

    [Fact]
    public async Task MissingStableIdentityNeverCreatesDurableRestoreCheckpoint()
    {
        var settings = new InMemorySettingsStore();
        var experience = Experience("desktop", ProjectorExperiencePersistence.Persistent);
        var firstRegistry = new ProjectorDisplayRegistry();
        var first = Display("display:6", null, ProjectorDisplayTrust.Private);
        firstRegistry.Upsert(first);
        using (var coordinator = new ProjectorSessionCoordinator(firstRegistry))
        using (var recovery = new ProjectorSessionRecoveryService(coordinator, firstRegistry, Catalog(experience), settings))
        {
            coordinator.Start(first);
            coordinator.Activate(experience);
            await recovery.FlushAsync(CancellationToken.None);
        }

        var secondRegistry = new ProjectorDisplayRegistry();
        var replacement = Display("display:6", null, ProjectorDisplayTrust.Private);
        secondRegistry.Upsert(replacement);
        using var secondCoordinator = new ProjectorSessionCoordinator(secondRegistry);
        using var secondRecovery = new ProjectorSessionRecoveryService(secondCoordinator, secondRegistry, Catalog(experience), settings);

        Assert.Null(await secondRecovery.RecoverAsync(replacement, CancellationToken.None));
        Assert.Null(secondCoordinator.Current);
    }

    [Fact]
    public async Task ExplicitStopClearsDurableRestoreCheckpoint()
    {
        var settings = new InMemorySettingsStore();
        var experience = Experience("desktop", ProjectorExperiencePersistence.Persistent);
        var display = Display("display:13", "monitor-stop", ProjectorDisplayTrust.Private);
        var firstRegistry = new ProjectorDisplayRegistry();
        firstRegistry.Upsert(display);
        using (var coordinator = new ProjectorSessionCoordinator(firstRegistry))
        using (var recovery = new ProjectorSessionRecoveryService(coordinator, firstRegistry, Catalog(experience), settings))
        {
            coordinator.Start(display);
            coordinator.Activate(experience);
            await recovery.FlushAsync(CancellationToken.None);
            coordinator.Stop();
            await recovery.FlushAsync(CancellationToken.None);
        }

        var secondRegistry = new ProjectorDisplayRegistry();
        var replacement = Display("display:14", "monitor-stop", ProjectorDisplayTrust.Private);
        secondRegistry.Upsert(replacement);
        using var secondCoordinator = new ProjectorSessionCoordinator(secondRegistry);
        using var secondRecovery = new ProjectorSessionRecoveryService(secondCoordinator, secondRegistry, Catalog(experience), settings);

        Assert.Null(await secondRecovery.RecoverAsync(replacement, CancellationToken.None));
        Assert.Null(secondCoordinator.Current);
    }

    private static async Task PersistActiveSessionAsync(InMemorySettingsStore settings, ProjectorExperience experience, ProjectorDisplay display)
    {
        var registry = new ProjectorDisplayRegistry();
        registry.Upsert(display);
        using var coordinator = new ProjectorSessionCoordinator(registry);
        using var recovery = new ProjectorSessionRecoveryService(coordinator, registry, Catalog(experience), settings);
        coordinator.Start(display);
        coordinator.Activate(experience);
        await recovery.FlushAsync(CancellationToken.None);
    }

    private static ProjectorExperienceCatalog Catalog(params ProjectorExperience[] experiences) => new([new FixedExperienceProvider(experiences)]);

    private static ProjectorExperience Experience(string id, ProjectorExperiencePersistence persistence, ProjectorContentSensitivity sensitivity = ProjectorContentSensitivity.Private, params ProjectorCapability[] requiredCapabilities)
    {
        var required = requiredCapabilities.Length == 0 ? new[] { ProjectorCapability.RenderHavenSurface } : requiredCapabilities;
        return new ProjectorExperience(id, id, "Test Projector experience", "projector", null, ProjectorExperienceSource.BuiltIn, ProjectorLaunchStrategy.HavenSurface, ProjectorInteractionProfile.Mixed, persistence, required, sensitivity);
    }

    private static ProjectorDisplay Display(string runtimeId, string? stableIdentity, ProjectorDisplayTrust trust, ProjectorCapabilityState controllerInput = ProjectorCapabilityState.Unknown) => new(
        runtimeId, stableIdentity, "External display", 1920, 1080, null, 60, 0, false, ProjectorTransportKind.NativeDisplay, ProjectorConnectionKind.Unknown, trust,
        ProjectorCapabilities.Unknown with { RenderHavenSurface = ProjectorCapabilityState.Available, PresentationDisplay = ProjectorCapabilityState.Available, ControllerInput = controllerInput }, DateTimeOffset.UtcNow);

    private sealed class FixedExperienceProvider(IReadOnlyList<ProjectorExperience> experiences) : IProjectorExperienceProvider
    {
        public ValueTask<IReadOnlyList<ProjectorExperience>> GetExperiencesAsync(ProjectorSessionSnapshot? session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<IReadOnlyList<ProjectorExperience>>(experiences);
        }
    }

    private sealed class InMemorySettingsStore : IVersionedSettingsStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) return Task.FromResult(_values.TryGetValue(key, out var value) ? value as T : null);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SettingsExportManifest());
        }

        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<SettingsImportResult>(new(true, manifest.Settings, "Imported in-memory test settings."));
        }
    }
}
