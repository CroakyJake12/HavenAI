using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>
/// Startup guard: builds the production infrastructure DI registrations and
/// resolves every service, so missing dependencies or throwing constructors
/// surface here instead of as runtime crashes on launch. Avalonia controls
/// are skipped (covered by the headless route tests).
/// </summary>
public sealed class ServiceContainerStartupTests
{
    [Fact]
    public async Task EveryRegisteredServiceResolvesWithoutThrowing()
    {
        var collection = new ServiceCollection();
        collection.AddHavenInfrastructure();
        collection.AddHavenPlannerInfrastructure();
        collection.AddHavenMesh();

        var descriptors = collection
            .Where(descriptor => !typeof(Avalonia.Controls.Control).IsAssignableFrom(descriptor.ServiceType)
                                 && !descriptor.ServiceType.ContainsGenericParameters)
            .GroupBy(descriptor => descriptor.ServiceType)
            .Select(group => group.First())
            .ToArray();

        var provider = collection.BuildServiceProvider();
        var failures = new List<string>();
        foreach (var descriptor in descriptors)
        {
            try
            {
                provider.GetService(descriptor.ServiceType);
            }
            catch (Exception exception)
            {
                failures.Add($"{descriptor.ServiceType.Name}: {exception.GetBaseException().Message}");
            }
        }

        await provider.DisposeAsync();

        Assert.True(failures.Count == 0,
            "Services failed to resolve:\n" + string.Join("\n", failures));
    }
}
