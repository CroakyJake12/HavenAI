using Haven.Application;

namespace Haven.Infrastructure;

public sealed class AppPaths : IAppPaths
{
    public AppPaths()
    {
        var custom = Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        DataDirectory = string.IsNullOrWhiteSpace(custom) ? Path.Combine(appData, "Haven") : Path.GetFullPath(custom);
        DatabasePath = Path.Combine(DataDirectory, "haven.db");
        BrowserProfileDirectory = Path.Combine(DataDirectory, "BrowserProfile");
        AttachmentsDirectory = Path.Combine(DataDirectory, "Attachments");
        LogsDirectory = Path.Combine(DataDirectory, "Logs");
        LegacyStatePath = Path.Combine(appData, "LocalCode", "state.json");

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BrowserProfileDirectory);
        Directory.CreateDirectory(AttachmentsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string BrowserProfileDirectory { get; }
    public string AttachmentsDirectory { get; }
    public string LogsDirectory { get; }
    public string LegacyStatePath { get; }
}
