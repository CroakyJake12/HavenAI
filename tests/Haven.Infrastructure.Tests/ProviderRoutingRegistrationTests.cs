using Haven.Application;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

public sealed class ProviderRoutingRegistrationTests
{
    [Fact]
    public async Task RoutedAndLocalClientsResolveWithoutCircularDependency()
    {
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        services.AddSingleton<IOllamaClient>(provider =>
            provider.GetRequiredService<IProviderModelClient>());

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var local = provider.GetRequiredService<ILocalOllamaClient>();
        var routed = provider.GetRequiredService<IProviderModelClient>();
        var compatibility = provider.GetRequiredService<IOllamaClient>();

        Assert.IsType<LocalOllamaClientAdapter>(local);
        Assert.IsType<ResilientProviderRoutingModelClient>(routed);
        Assert.Same(routed, compatibility);
        Assert.NotSame(local, routed);
    }
}
