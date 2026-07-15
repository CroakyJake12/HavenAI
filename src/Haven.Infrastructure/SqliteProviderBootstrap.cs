namespace Haven.Infrastructure;

internal static class SqliteProviderBootstrap
{
    private static readonly Lazy<bool> Initializer = new(
        static () =>
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
            return true;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureInitialized() => _ = Initializer.Value;
}
