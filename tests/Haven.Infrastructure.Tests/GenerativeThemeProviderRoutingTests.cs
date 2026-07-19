/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/GenerativeThemeProviderRoutingTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns GenerativeThemeProviderRoutingTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents generative theme provider routing tests and keeps its related state and behavior together.
/// </summary>
public sealed class GenerativeThemeProviderRoutingTests
{
    /// <summary>
    /// Performs the production di uses the provider routed client for theme studio step owned by this component.
    /// </summary>
    [Fact]
    public async Task ProductionDiUsesTheProviderRoutedClientForThemeStudio()
    {
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        await using var provider = services.BuildServiceProvider();

        var providerClient = provider.GetRequiredService<IProviderModelClient>();
        var resilientClient = provider.GetRequiredService<ResilientProviderRoutingModelClient>();
        var themeStudio = provider.GetRequiredService<IGenerativeThemeAiService>();

        Assert.Same(resilientClient, providerClient);
        Assert.IsType<GenerativeThemeAiService>(themeStudio);
    }
}
