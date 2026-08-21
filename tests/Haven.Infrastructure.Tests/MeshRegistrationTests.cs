using Haven.Application;
using Haven.Application.Automations;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

public sealed class MeshRegistrationTests
{
    [Fact]
    public async Task MeshServicesResolveWithoutCircularDependencyAndJoinProviderCollections()
    {
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        services.AddHavenMesh();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.NotNull(provider.GetRequiredService<MeshCoordinator>());
        Assert.Contains(provider.GetServices<IDeviceActionProvider>(), item => item is WindowsComputerDeviceActionProvider);
        Assert.Contains(provider.GetServices<IDeviceActionProvider>(), item => item is MeshDeviceActionProvider);
        Assert.Contains(provider.GetServices<IModelProvider>(), item => item is MeshRemoteModelProvider);
    }
}
