using Haven.Application;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

public sealed class GenerativeThemeProviderRoutingTests
{
    [Fact]
    public async Task ProductionDiUsesTheProviderRoutedClientForThemeStudio()
    {
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        await using var provider = services.BuildServiceProvider();

        var providerClient = provider.GetRequiredService<IProviderModelClient>();
        var concreteClient = provider.GetRequiredService<ProviderRoutingModelClient>();
        var themeStudio = provider.GetRequiredService<IGenerativeThemeAiService>();

        Assert.Same(concreteClient, providerClient);
        Assert.IsType<GenerativeThemeAiService>(themeStudio);
    }
}
