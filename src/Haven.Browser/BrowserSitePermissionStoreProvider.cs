/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Browser/BrowserSitePermissionStoreProvider.cs, in the Browser layer, which isolates browser state, safety policy, transport, and automation.
 * What: This file owns BrowserSitePermissionStoreProvider. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Browser capabilities are isolated behind explicit policy boundaries because navigation and automation process untrusted external content.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.Concurrent;
using Haven.Application;

namespace Haven.Browser;

/// <summary>
/// Represents browser site permission store provider and keeps its related state and behavior together.
/// </summary>
public static class BrowserSitePermissionStoreProvider
{
    /// <summary>
    /// Stores stores locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Lazy<BrowserSitePermissionStore>> Stores =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Retrieves this member for the current operation.
    /// </summary>
    public static BrowserSitePermissionStore Get(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var key = Path.GetFullPath(paths.DataDirectory);
        return Stores.GetOrAdd(key, _ => new Lazy<BrowserSitePermissionStore>(
            () => new BrowserSitePermissionStore(paths),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }
}
