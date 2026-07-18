/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/ProviderRoutingRegistrationTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ProviderRoutingRegistrationTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents provider routing registration tests and keeps its related state and behavior together.
/// </summary>
public sealed class ProviderRoutingRegistrationTests
{
    /// <summary>
    /// Performs the routed and local clients resolve without circular dependency step owned by this component.
    /// </summary>
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
