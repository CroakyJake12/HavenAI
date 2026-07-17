using System.Collections.Concurrent;
using Haven.Application;

namespace Haven.Browser;

public static class BrowserSitePermissionStoreProvider
{
    private static readonly ConcurrentDictionary<string, Lazy<BrowserSitePermissionStore>> Stores =
        new(StringComparer.OrdinalIgnoreCase);

    public static BrowserSitePermissionStore Get(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var key = Path.GetFullPath(paths.DataDirectory);
        return Stores.GetOrAdd(key, _ => new Lazy<BrowserSitePermissionStore>(
            () => new BrowserSitePermissionStore(paths),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }
}
