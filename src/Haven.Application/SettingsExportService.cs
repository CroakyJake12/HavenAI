using System.Text.Json;

namespace Haven.Application;

public sealed class SettingsExportService
{
    private readonly SettingsEncryptionService _encryption;

    public SettingsExportService(SettingsEncryptionService encryption)
    {
        _encryption = encryption;
    }

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

public sealed class SettingsExportData
{
    public int Version { get; set; }
    public DateTimeOffset ExportedAt { get; set; }
    public string AppVersion { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();
}

public sealed record SettingsExportResult(bool Succeeded, string? Data, string Message);
public sealed record SettingsImportResult(bool Succeeded, IReadOnlyDictionary<string, string>? Settings, string Message);
