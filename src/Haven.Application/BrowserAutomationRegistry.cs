/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/BrowserAutomationRegistry.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns BrowserAutomationRegistry, Holder. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Runtime.CompilerServices;

namespace Haven.Application;

/// <summary>
/// Represents browser automation registry and keeps its related state and behavior together.
/// </summary>
public static class BrowserAutomationRegistry
{
    /// <summary>
    /// Stores registrations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly ConditionalWeakTable<IBrowserToolService, Holder> Registrations = new();
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly object Gate = new();

    /// <summary>
    /// Performs the register step owned by this component.
    /// </summary>
    public static void Register(IBrowserToolService browser, IBrowserAutomationService automation)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(automation);
        lock (Gate)
        {
            Registrations.Remove(browser);
            Registrations.Add(browser, new Holder(automation));
        }
    }

    /// <summary>
    /// Performs the resolve step owned by this component.
    /// </summary>
    public static IBrowserAutomationService Resolve(IBrowserToolService browser)
    {
        ArgumentNullException.ThrowIfNull(browser);
        return Registrations.TryGetValue(browser, out var holder)
            ? holder.Automation
            : throw new InvalidOperationException("Browser automation has not been attached to this browser session.");
    }

    /// <summary>
    /// Represents holder and keeps its related state and behavior together.
    /// </summary>
    private sealed record Holder(IBrowserAutomationService Automation);
}
