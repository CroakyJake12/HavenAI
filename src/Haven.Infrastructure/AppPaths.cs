/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/AppPaths.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns AppPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Represents app paths and keeps its related state and behavior together.
/// </summary>
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

    /// <summary>
    /// Gets or updates data directory, the bindable or domain state represented by this property.
    /// </summary>
    public string DataDirectory { get; }
    /// <summary>
    /// Gets or updates database path, the bindable or domain state represented by this property.
    /// </summary>
    public string DatabasePath { get; }
    /// <summary>
    /// Gets or updates browser profile directory, the bindable or domain state represented by this property.
    /// </summary>
    public string BrowserProfileDirectory { get; }
    /// <summary>
    /// Gets or updates attachments directory, the bindable or domain state represented by this property.
    /// </summary>
    public string AttachmentsDirectory { get; }
    /// <summary>
    /// Gets or updates logs directory, the bindable or domain state represented by this property.
    /// </summary>
    public string LogsDirectory { get; }
    /// <summary>
    /// Gets or updates legacy state path, the bindable or domain state represented by this property.
    /// </summary>
    public string LegacyStatePath { get; }
}
