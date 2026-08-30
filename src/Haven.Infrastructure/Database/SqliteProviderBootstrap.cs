/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/SqliteProviderBootstrap.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns SqliteProviderBootstrap. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */
namespace Haven.Infrastructure;
/// <summary>
/// Represents sqlite provider bootstrap and keeps its related state and behavior together.
/// </summary>
internal static class SqliteProviderBootstrap
{
    /// <summary>
    /// Stores initializer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Lazy<bool> Initializer = new(
        static () =>
        {
            if (OperatingSystem.IsAndroid())
            {
                // Android initializes SQLitePCLRaw.bundle_e_sqlite3 in AndroidApp
                // before Avalonia constructs Haven's infrastructure services.
                return true;
            }

            if (OperatingSystem.IsWindows())
            {
                SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
            }
            else
            {
                SQLitePCL.Batteries_V2.Init();
            }
            return true;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);
    /// <summary>
    /// Performs the ensure initialized step owned by this component.
    /// </summary>
    public static void EnsureInitialized() => _ = Initializer.Value;
}
