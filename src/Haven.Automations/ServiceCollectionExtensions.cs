/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Automations/ServiceCollectionExtensions.cs, in the Automations layer, which parses schedules and runs durable background actions.
 * What: This file owns ServiceCollectionExtensions. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Automations;

/// <summary>
/// Represents service collection extensions and keeps its related state and behavior together.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Performs the add haven automations step owned by this component.
    /// </summary>
    public static IServiceCollection AddHavenAutomations(this IServiceCollection services)
    {
        services.AddSingleton<ScheduleCalculator>();
        services.AddSingleton<IAutomationDeliveryOutbox, AutomationDeliveryOutbox>();
        services.AddSingleton<AutomationRunner>();
        services.AddSingleton<WindowsAutomationRegistrationService>();
        return services;
    }
}
