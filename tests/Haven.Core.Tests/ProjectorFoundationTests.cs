using Haven.Application;

namespace Haven.Core.Tests;

public sealed class ProjectorFoundationTests
{
    [Fact]
    public void DisplayRegistryPublishesMeaningfulChangesButNotObservationRefreshes()
    {
        var registry = new ProjectorDisplayRegistry();
        var changes = new List<ProjectorDisplayChange>();
        registry.Changed += changes.Add;
        var display = Display("android-display:2", "panel-a");
        registry.Upsert(display);
        registry.Upsert(display with { ObservedAt = display.ObservedAt.AddSeconds(1) });
        registry.Upsert(display with { WidthPixels = 2560, ObservedAt = display.ObservedAt.AddSeconds(2) });
        registry.Remove(display.RuntimeId);
        Assert.Equal(new[] { ProjectorDisplayChangeKind.Added, ProjectorDisplayChangeKind.Changed, ProjectorDisplayChangeKind.Removed }, changes.Select(change => change.Kind));
    }

    [Fact]
    public void RuntimeIdReuseWithoutStableIdentityDoesNotRestoreDisconnectedSession()
    {
        var registry = new ProjectorDisplayRegistry();
        using var coordinator = new ProjectorSessionCoordinator(registry);
        var display = Display("android-display:4", null);
        registry.Upsert(display);
        coordinator.Start(display);
        registry.Remove(display.RuntimeId);
        registry.Upsert(Display("android-display:4", null));
        Assert.Equal(ProjectorSessionState.Disconnected, coordinator.Current?.State);
        Assert.False(coordinator.TryReconnect(Display("android-display:4", null), out _));
    }

    [Fact]
    public void StableIdentityReconnectFallsBackToGalleryUntilExperienceIsRevalidated()
    {
        var registry = new ProjectorDisplayRegistry();
        using var coordinator = new ProjectorSessionCoordinator(registry);
        var first = Display("android-display:7", "monitor-a");
        registry.Upsert(first);
        coordinator.Start(first);
        coordinator.Activate(Experience("desktop"));
        registry.Remove(first.RuntimeId);
        var replacement = Display("android-display:11", "monitor-a");

        Assert.True(coordinator.TryReconnect(replacement, out var restored));

        Assert.Equal(ProjectorSessionState.Gallery, restored?.State);
        Assert.Null(restored?.CurrentExperienceId);
        Assert.Equal("desktop", restored?.PreviousExperienceId);
        Assert.Equal(replacement.RuntimeId, restored?.TargetDisplay.RuntimeId);
    }

    [Fact]
    public void ReturnToGalleryClearsActiveExperienceAndPreservesHistory()
    {
        var registry = new ProjectorDisplayRegistry();
        using var coordinator = new ProjectorSessionCoordinator(registry);
        var display = Display("android-display:5", "monitor-b") with
        {
            Capabilities = ProjectorCapabilities.Unknown with
            {
                PresentationDisplay = ProjectorCapabilityState.Available,
                RenderHavenSurface = ProjectorCapabilityState.Available
            }
        };
        registry.Upsert(display);
        coordinator.Start(display);
        coordinator.Activate(Experience("desktop", ProjectorCapability.RenderHavenSurface),
            new ProjectorControllerDefinition("desktop-controls", [new ProjectorControllerAction("home", "Home", "browse", "desktop.home")]));

        var gallery = coordinator.ReturnToGallery();

        Assert.Equal(ProjectorSessionState.Gallery, gallery.State);
        Assert.Null(gallery.CurrentExperienceId);
        Assert.Equal("desktop", gallery.PreviousExperienceId);
        Assert.Null(gallery.Controller);
    }

    [Fact]
    public void UnknownRequiredCapabilityKeepsExperienceUnavailable()
    {
        var experience = Experience("app", ProjectorCapability.LaunchAndroidActivity);
        Assert.False(experience.IsAvailable(ProjectorCapabilities.Unknown));
        Assert.True(experience.IsAvailable(ProjectorCapabilities.Unknown with { LaunchAndroidActivity = ProjectorCapabilityState.Available }));
    }

    [Fact]
    public async Task BuiltInProviderOnlyPublishesHostBackedProjectorExperiences()
    {
        var provider = new BuiltInProjectorExperienceProvider();
        var experiences = await provider.GetExperiencesAsync(null, CancellationToken.None);

        // Execution parity: built-ins are advertised only when a production
        // Projector host exists, so the shared HavenSurface experience must be
        // the sole built-in until additional real executors ship.
        Assert.Equal(new[] { "desktop" }, experiences.Select(experience => experience.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(experiences, experience => Assert.Contains(ProjectorCapability.RenderHavenSurface, experience.RequiredCapabilities));
    }

    [Fact]
    public async Task ExperienceCatalogFiltersUnavailableItemsAndKeepsProviderOrderForDuplicateIds()
    {
        var duplicateDesktop = Experience("desktop");
        var catalog = new ProjectorExperienceCatalog(
        [
            new BuiltInProjectorExperienceProvider(),
            new FixedExperienceProvider([duplicateDesktop])
        ]);

        var unavailableDisplay = Display("android-display:8", null);
        var unavailableSession = Session(unavailableDisplay);
        Assert.Empty(await catalog.GetExperiencesAsync(unavailableSession, CancellationToken.None));

        var availableDisplay = unavailableDisplay with
        {
            Capabilities = unavailableDisplay.Capabilities with
            {
                RenderHavenSurface = ProjectorCapabilityState.Available
            }
        };
        var available = await catalog.GetExperiencesAsync(Session(availableDisplay), CancellationToken.None);

        Assert.Single(available);
        Assert.Equal("Desktop", Assert.Single(available, experience => experience.Id == "desktop").Name);
    }

    private static ProjectorDisplay Display(string runtimeId, string? stableIdentity) => new(
        runtimeId, stableIdentity, "External display", 1920, 1080, null, 60, 0, false,
        ProjectorTransportKind.NativeDisplay, ProjectorConnectionKind.Unknown, ProjectorDisplayTrust.Private,
        ProjectorCapabilities.Unknown with { PresentationDisplay = ProjectorCapabilityState.Available }, DateTimeOffset.UtcNow);

    private static ProjectorSessionSnapshot Session(ProjectorDisplay display) => new(
        Guid.NewGuid(),
        ProjectorSessionState.Gallery,
        display,
        CurrentExperienceId: null,
        PreviousExperienceId: null,
        Controller: null,
        WorkspaceId: null,
        InputDeviceKinds: [],
        StartedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    private static ProjectorExperience Experience(string id, params ProjectorCapability[] required) => new(
        id, id, "Test experience", "projector", null, ProjectorExperienceSource.BuiltIn, ProjectorLaunchStrategy.HavenSurface,
        ProjectorInteractionProfile.Mixed, ProjectorExperiencePersistence.Session, required);

    private sealed class FixedExperienceProvider(IReadOnlyList<ProjectorExperience> experiences) : IProjectorExperienceProvider
    {
        public ValueTask<IReadOnlyList<ProjectorExperience>> GetExperiencesAsync(
            ProjectorSessionSnapshot? session,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<IReadOnlyList<ProjectorExperience>>(experiences);
        }
    }
}
