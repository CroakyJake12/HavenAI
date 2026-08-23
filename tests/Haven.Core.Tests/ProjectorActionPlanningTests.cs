using Haven.Application;

namespace Haven.Core.Tests;

public sealed class ProjectorActionPlanningTests
{
    [Fact]
    public async Task PublicDisplayDoesNotAdvertisePrivateBuiltInWithoutPublicHost()
    {
        IProjectorExperienceProvider[] providers = [new BuiltInProjectorExperienceProvider()];
        var catalog = new ProjectorExperienceCatalog(providers);
        var session = Session(Display(ProjectorDisplayTrust.Public, render: true));

        var available = await catalog.GetExperiencesAsync(session, CancellationToken.None);

        Assert.Empty(available);
    }

    [Fact]
    public async Task ExplicitPhoneSideTrustChangeAdvertisesOnlyHostedBuiltIn()
    {
        var registry = new ProjectorDisplayRegistry();
        using var sessions = new ProjectorSessionCoordinator(registry);
        var display = Display(ProjectorDisplayTrust.Public, render: true);
        registry.Upsert(display);
        sessions.Start(display);

        var updated = sessions.SetTargetTrust(ProjectorDisplayTrust.Private);
        var catalog = new ProjectorExperienceCatalog([new BuiltInProjectorExperienceProvider()]);
        var available = await catalog.GetExperiencesAsync(updated, CancellationToken.None);

        Assert.Equal(ProjectorDisplayTrust.Private, updated.TargetDisplay.Trust);
        Assert.Equal(ProjectorDisplayTrust.Private, registry.Get(display.RuntimeId)?.Trust);
        var desktop = Assert.Single(available);
        Assert.Equal("desktop", desktop.Id);
        Assert.DoesNotContain(available, experience => experience.Id == "browser");
        Assert.DoesNotContain(available, experience => experience.Id == "study");
        Assert.DoesNotContain(available, experience => experience.Id == "presentation");
    }

    [Fact]
    public async Task PlannerRoutesNaturalLanguageToHostedDesktopExperience()
    {
        var planner = Planner(out _, out var session);

        var plan = await planner.PlanAsync("Please turn this screen into desktop", session, CancellationToken.None);

        Assert.True(plan.CanExecute);
        Assert.Equal(ProjectorActionPlanStatus.Ready, plan.Status);
        Assert.Equal(ProjectorActionKind.OpenExperience, plan.Action);
        Assert.Equal("desktop", plan.TargetExperienceId);
    }

    [Fact]
    public async Task PlannerDoesNotRouteRemovedUnhostedBuiltInExperience()
    {
        var planner = Planner(out _, out var session);

        var plan = await planner.PlanAsync("Please turn this screen into study", session, CancellationToken.None);

        Assert.False(plan.CanExecute);
        Assert.Equal(ProjectorActionPlanStatus.Unsupported, plan.Status);
        Assert.Null(plan.Action);
        Assert.Null(plan.TargetExperienceId);
    }

    [Fact]
    public async Task PlannerBlocksPrivateDesktopOnPublicDisplayWithoutFakeFallback()
    {
        IProjectorExperienceProvider[] providers = [new BuiltInProjectorExperienceProvider()];
        var catalog = new ProjectorExperienceCatalog(providers);
        var planner = new ProjectorActionPlanner(catalog, providers);
        var session = Session(Display(ProjectorDisplayTrust.Public, render: true));

        var plan = await planner.PlanAsync("open desktop", session, CancellationToken.None);

        Assert.False(plan.CanExecute);
        Assert.Equal(ProjectorActionPlanStatus.BlockedByTrust, plan.Status);
        Assert.Equal("desktop", plan.TargetExperienceId);
        Assert.Null(plan.FallbackExperienceId);
        Assert.Null(plan.Action);
    }

    [Fact]
    public async Task PlannerRejectsUnknownRequestInsteadOfInventingDeviceControl()
    {
        var planner = Planner(out _, out var session);

        var plan = await planner.PlanAsync("toggle hidden system compositor flags", session, CancellationToken.None);

        Assert.False(plan.CanExecute);
        Assert.Equal(ProjectorActionPlanStatus.Unsupported, plan.Status);
        Assert.Null(plan.Action);
        Assert.Null(plan.TargetExperienceId);
    }

    [Fact]
    public async Task PlannerMapsGeneratedExperienceToGeneratedExecutor()
    {
        var generated = new ProjectorExperience(
            "genui:0123456789abcdef0123456789abcdef",
            "Generated Brief",
            "Saved generated experience",
            "apps",
            null,
            ProjectorExperienceSource.GeneratedUi,
            ProjectorLaunchStrategy.GeneratedUi,
            ProjectorInteractionProfile.Mixed,
            ProjectorExperiencePersistence.Persistent,
            [ProjectorCapability.RenderHavenSurface]);
        IProjectorExperienceProvider[] providers = [new FixedExperienceProvider([generated])];
        var catalog = new ProjectorExperienceCatalog(providers);
        var planner = new ProjectorActionPlanner(catalog, providers);

        var plan = await planner.PlanAsync("open Generated Brief", Session(Display(ProjectorDisplayTrust.Private, render: true)), CancellationToken.None);

        Assert.True(plan.CanExecute);
        Assert.Equal(ProjectorActionKind.OpenGeneratedExperience, plan.Action);
        Assert.Equal(generated.Id, plan.TargetExperienceId);
    }

    [Fact]
    public async Task PlannerMapsRemoteExperienceToRemoteExecutor()
    {
        var remote = new ProjectorExperience(
            "remote-projector:0123456789abcdef0123456789abcdef:616e64726f69642d646973706c61793a32",
            "Living Room Projector",
            "Acknowledged remote Projector destination",
            "studio",
            null,
            ProjectorExperienceSource.RemoteDevice,
            ProjectorLaunchStrategy.RemoteDevice,
            ProjectorInteractionProfile.Desktop,
            ProjectorExperiencePersistence.Session,
            []);
        IProjectorExperienceProvider[] providers = [new FixedExperienceProvider([remote])];
        var catalog = new ProjectorExperienceCatalog(providers);
        var planner = new ProjectorActionPlanner(catalog, providers);

        var plan = await planner.PlanAsync("route Living Room Projector", Session(Display(ProjectorDisplayTrust.Private, render: true)), CancellationToken.None);

        Assert.True(plan.CanExecute);
        Assert.Equal(ProjectorActionKind.RouteRemoteExperience, plan.Action);
        Assert.Equal(remote.Id, plan.TargetExperienceId);
    }

    [Fact]
    public async Task PlannerRoutesOnlyDeclaredApplicationWhenLaunchCapabilityIsProven()
    {
        var app = ApplicationExperience("android-app:notes/main", "Notes");
        IProjectorExperienceProvider[] providers =
        [
            new BuiltInProjectorExperienceProvider(),
            new FixedExperienceProvider([app])
        ];
        var catalog = new ProjectorExperienceCatalog(providers);
        var planner = new ProjectorActionPlanner(catalog, providers);
        var display = Display(ProjectorDisplayTrust.Private, render: true) with
        {
            Capabilities = Display(ProjectorDisplayTrust.Private, render: true).Capabilities with
            {
                LaunchAndroidActivity = ProjectorCapabilityState.Available
            }
        };

        var plan = await planner.PlanAsync("launch Notes", Session(display), CancellationToken.None);

        Assert.True(plan.CanExecute);
        Assert.Equal(ProjectorActionKind.LaunchApplication, plan.Action);
        Assert.Equal(app.Id, plan.TargetExperienceId);
    }

    [Fact]
    public async Task PlannerBlocksDeclaredApplicationAfterDisplayTrustDropsToPublic()
    {
        var app = ApplicationExperience("android-app:notes/main", "Notes");
        IProjectorExperienceProvider[] providers = [new FixedExperienceProvider([app])];
        var catalog = new ProjectorExperienceCatalog(providers);
        var planner = new ProjectorActionPlanner(catalog, providers);
        var display = Display(ProjectorDisplayTrust.Private, render: true) with
        {
            Capabilities = Display(ProjectorDisplayTrust.Private, render: true).Capabilities with
            {
                LaunchAndroidActivity = ProjectorCapabilityState.Available
            }
        };
        var privateSession = Session(display);

        var privatePlan = await planner.PlanAsync("open Notes", privateSession, CancellationToken.None);
        var publicPlan = await planner.PlanAsync(
            "open Notes",
            privateSession with { TargetDisplay = display with { Trust = ProjectorDisplayTrust.Public } },
            CancellationToken.None);

        Assert.True(privatePlan.CanExecute);
        Assert.False(publicPlan.CanExecute);
        Assert.Equal(ProjectorActionPlanStatus.BlockedByTrust, publicPlan.Status);
        Assert.Equal(app.Id, publicPlan.TargetExperienceId);
    }

    [Fact]
    public async Task PlannerReportsCapabilityBlockForDeclaredApplicationWithoutLaunchProof()
    {
        var app = ApplicationExperience("android-app:notes/main", "Notes");
        IProjectorExperienceProvider[] providers = [new FixedExperienceProvider([app])];
        var catalog = new ProjectorExperienceCatalog(providers);
        var planner = new ProjectorActionPlanner(catalog, providers);
        var session = Session(Display(ProjectorDisplayTrust.Private, render: true));

        var plan = await planner.PlanAsync("open Notes", session, CancellationToken.None);

        Assert.False(plan.CanExecute);
        Assert.Equal(ProjectorActionPlanStatus.BlockedByCapability, plan.Status);
        Assert.Equal(app.Id, plan.TargetExperienceId);
    }

    private static ProjectorActionPlanner Planner(
        out IProjectorExperienceProvider[] providers,
        out ProjectorSessionSnapshot session)
    {
        providers = [new BuiltInProjectorExperienceProvider()];
        var catalog = new ProjectorExperienceCatalog(providers);
        session = Session(Display(ProjectorDisplayTrust.Private, render: true));
        return new ProjectorActionPlanner(catalog, providers);
    }

    private static ProjectorDisplay Display(ProjectorDisplayTrust trust, bool render)
    {
        var capabilities = ProjectorCapabilities.Unknown with
        {
            RenderHavenSurface = render ? ProjectorCapabilityState.Available : ProjectorCapabilityState.Unknown,
            PresentationDisplay = ProjectorCapabilityState.Available
        };
        return new ProjectorDisplay(
            "android-display:2",
            "monitor-a",
            "External display",
            1920,
            1080,
            null,
            60,
            0,
            false,
            ProjectorTransportKind.NativeDisplay,
            ProjectorConnectionKind.Unknown,
            trust,
            capabilities,
            DateTimeOffset.UtcNow);
    }

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

    private static ProjectorExperience ApplicationExperience(string id, string name) => new(
        id,
        name,
        "Installed Android application",
        "apps",
        null,
        ProjectorExperienceSource.Application,
        ProjectorLaunchStrategy.AndroidApplication,
        ProjectorInteractionProfile.Mixed,
        ProjectorExperiencePersistence.Session,
        [ProjectorCapability.LaunchAndroidActivity]);

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
