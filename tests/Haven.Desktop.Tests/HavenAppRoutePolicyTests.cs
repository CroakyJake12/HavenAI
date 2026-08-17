using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.Tests;

public sealed class HavenAppRoutePolicyTests
{
    private static readonly string[] ExpectedBuiltInKeys =
    [
        "chat", "study", "automations", "tasks", "studio", "browse", "plan", "training", "imagine", "canvas",
        "present", "data", "vision", "play", "translate", "launcher", "go", "dashboard", "write"
    ];

    public static TheoryData<string, HavenAppRouteKind, HavenSurface> BuiltInRoutes => new()
    {
        { "chat", HavenAppRouteKind.BaseMode, HavenSurface.Chat },
        { "study", HavenAppRouteKind.BaseMode, HavenSurface.Study },
        { "automations", HavenAppRouteKind.Automations, HavenSurface.Automations },
        { "tasks", HavenAppRouteKind.BaseMode, HavenSurface.Tasks },
        { "studio", HavenAppRouteKind.BaseMode, HavenSurface.Studio },
        { "browse", HavenAppRouteKind.Browse, HavenSurface.Browse },
        { "plan", HavenAppRouteKind.Plan, HavenSurface.Plan },
        { "training", HavenAppRouteKind.Training, HavenSurface.Training },
        { "imagine", HavenAppRouteKind.Imagine, HavenSurface.Imagine },
        { "write", HavenAppRouteKind.ModeWorkspace, HavenSurface.Write },
        { "canvas", HavenAppRouteKind.ModeWorkspace, HavenSurface.Canvas },
        { "present", HavenAppRouteKind.ModeWorkspace, HavenSurface.Present },
        { "data", HavenAppRouteKind.ModeWorkspace, HavenSurface.Data },
        { "vision", HavenAppRouteKind.Vision, HavenSurface.Vision },
        { "play", HavenAppRouteKind.ModeWorkspace, HavenSurface.Play },
        { "translate", HavenAppRouteKind.ModeWorkspace, HavenSurface.Translate },
        { "launcher", HavenAppRouteKind.ModeWorkspace, HavenSurface.Launcher },
        { "go", HavenAppRouteKind.Go, HavenSurface.Go },
        { "dashboard", HavenAppRouteKind.Dashboard, HavenSurface.Dashboard }
    };

    [Theory]
    [MemberData(nameof(BuiltInRoutes))]
    public void EveryBuiltInAppHasTheExpectedConcreteRoute(
        string key,
        HavenAppRouteKind expectedKind,
        HavenSurface expectedSurface)
    {
        var app = Assert.Single(BuiltInModeSeed.Modes, item => item.Key == key);

        var route = HavenAppRoutePolicy.Resolve(app);

        Assert.Equal(expectedKind, route.Kind);
        Assert.Equal(expectedSurface, route.Surface);
    }

    [Fact]
    public void RouteMatrixCoversTheCompleteBuiltInInventory()
    {
        var registeredKeys = BuiltInModeSeed.Modes.Select(item => item.Key).Order().ToArray();

        Assert.Equal(ExpectedBuiltInKeys.Order(), registeredKeys);
    }

    [Fact]
    public void UserAppsFallBackToTheirCompatibleBaseMode()
    {
        var now = DateTimeOffset.UtcNow;
        var app = new ModeDefinition(
            Guid.NewGuid(), "my-study-app", "My Study App", "", "study",
            HavenMode.Study, "[]", "[]", "[]", "[]", "",
            ModeSource.Created, ModeInstallState.InstalledByUser, "User", "1.0.0", "[]", now, now);

        var route = HavenAppRoutePolicy.Resolve(app);

        Assert.Equal(HavenAppRouteKind.BaseMode, route.Kind);
        Assert.Equal(HavenSurface.Study, route.Surface);
    }
}
