using Haven.Application;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

public sealed class PlannerServiceRegistrationTests
{
    [Fact]
    public void Desktop_startup_composition_resolves_the_study_planner_service()
    {
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        services.AddHavenPlannerInfrastructure();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<StudyPlannerService>(provider.GetRequiredService<IStudyPlannerService>());
    }
}
