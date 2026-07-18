/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/SettingsExportService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns SettingsExportService, SettingsExportData, SettingsExportResult, SettingsImportResult. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;

namespace Haven.Application;

/// <summary>
/// Represents settings export service and keeps its related state and behavior together.
/// </summary>
public sealed class SettingsExportService
{
    /// <summary>
    /// Stores encryption locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SettingsEncryptionService _encryption;

    public SettingsExportService(SettingsEncryptionService encryption)
    {
        _encryption = encryption;
    }

    /// <summary>
    /// Performs export async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<SettingsExportResult> ExportAsync(
        IReadOnlyDictionary<string, string> settings,
        string? encryptionPassphrase,
        CancellationToken cancellationToken)
    {
        try
        {
            var exportData = new SettingsExportData
            {
                Version = 1,
                ExportedAt = DateTimeOffset.UtcNow,
                Settings = settings,
                AppVersion = "1.0.0"
            };

            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });

            if (!string.IsNullOrWhiteSpace(encryptionPassphrase))
                json = _encryption.Encrypt(json, encryptionPassphrase);

            return new SettingsExportResult(true, json, "Settings exported successfully.");
        }
        catch (Exception ex)
        {
            return new SettingsExportResult(false, null, $"Export failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Performs import async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<SettingsImportResult> ImportAsync(
        string data,
        string? encryptionPassphrase,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(encryptionPassphrase))
                data = _encryption.Decrypt(data, encryptionPassphrase);

            var importData = JsonSerializer.Deserialize<SettingsExportData>(data);
            if (importData is null)
                return new SettingsImportResult(false, null, "Invalid settings data.");

            if (importData.Version > 1)
                return new SettingsImportResult(false, null, $"Settings version {importData.Version} is not supported.");

            var validatedSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in importData.Settings)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                    validatedSettings[kvp.Key] = kvp.Value;
            }

            return new SettingsImportResult(true, validatedSettings, $"Imported {validatedSettings.Count} settings from {importData.ExportedAt:yyyy-MM-dd}.");
        }
        catch (JsonException)
        {
            return new SettingsImportResult(false, null, "Settings data is corrupted or invalid.");
        }
        catch (Exception ex)
        {
            return new SettingsImportResult(false, null, $"Import failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Represents settings export data and keeps its related state and behavior together.
/// </summary>
public sealed class SettingsExportData
{
    /// <summary>
    /// Gets or updates version, the bindable or domain state represented by this property.
    /// </summary>
    public int Version { get; set; }
    /// <summary>
    /// Gets or updates exported at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset ExportedAt { get; set; }
    /// <summary>
    /// Gets or updates app version, the bindable or domain state represented by this property.
    /// </summary>
    public string AppVersion { get; set; } = string.Empty;
    /// <summary>
    /// Gets or updates settings, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyDictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();
}

/// <summary>
/// Represents settings export result and keeps its related state and behavior together.
/// </summary>
public sealed record SettingsExportResult(bool Succeeded, string? Data, string Message);
/// <summary>
/// Represents settings import result and keeps its related state and behavior together.
/// </summary>
public sealed record SettingsImportResult(bool Succeeded, IReadOnlyDictionary<string, string>? Settings, string Message);
