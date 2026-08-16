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
    public void StableIdentityRestoresActiveExperienceAcrossRuntimeIdChange()
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
        Assert.Equal(ProjectorSessionState.Active, restored?.State);
        Assert.Equal("desktop", restored?.CurrentExperienceId);
        Assert.Equal(replacement.RuntimeId, restored?.TargetDisplay.RuntimeId);
    }

    [Fact]
    public void UnknownRequiredCapabilityKeepsExperienceUnavailable()
    {
        var experience = Experience("app", ProjectorCapability.LaunchAndroidActivity);
        Assert.False(experience.IsAvailable(ProjectorCapabilities.Unknown));
        Assert.True(experience.IsAvailable(ProjectorCapabilities.Unknown with { LaunchAndroidActivity = ProjectorCapabilityState.Available }));
    }

    private static ProjectorDisplay Display(string runtimeId, string? stableIdentity) => new(
        runtimeId, stableIdentity, "External display", 1920, 1080, null, 60, 0, false,
        ProjectorTransportKind.NativeDisplay, ProjectorConnectionKind.Unknown, ProjectorDisplayTrust.Public,
        ProjectorCapabilities.Unknown with { PresentationDisplay = ProjectorCapabilityState.Available }, DateTimeOffset.UtcNow);

    private static ProjectorExperience Experience(string id, params ProjectorCapability[] required) => new(
        id, id, "Test experience", "projector", null, ProjectorExperienceSource.BuiltIn, ProjectorLaunchStrategy.HavenSurface,
        ProjectorInteractionProfile.Mixed, ProjectorExperiencePersistence.Session, required);
}
