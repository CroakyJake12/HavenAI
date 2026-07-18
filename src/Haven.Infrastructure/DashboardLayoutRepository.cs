/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/DashboardLayoutRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns DashboardLayoutRepository. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents dashboard layout repository and keeps its related state and behavior together.
/// </summary>
public sealed class DashboardLayoutRepository(ISqliteConnectionFactory factory) : IDashboardLayoutRepository
{
    /// <summary>
    /// Stores setting key locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const string SettingKey = "dashboard.layout.v1";
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Retrieves async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<DashboardTileLayout>> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key=$key;";
        command.Parameters.AddWithValue("$key", SettingKey);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (string.IsNullOrWhiteSpace(value)) return [];
        try
        {
            return (JsonSerializer.Deserialize<List<DashboardTileLayout>>(value, JsonOptions) ?? [])
                .Where(IsValid).OrderBy(item => item.Order).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Performs save asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task SaveAsync(IReadOnlyList<DashboardTileLayout> layout, CancellationToken cancellationToken)
    {
        var normalized = layout.Where(IsValid).GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) => group.Last() with { Version = 1, Order = index }).ToArray();
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key,value,updated_at) VALUES($key,$value,$updatedAt)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", SettingKey);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(normalized, JsonOptions));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports whether valid applies to the current state.
    /// </summary>
    private static bool IsValid(DashboardTileLayout item) =>
        item.Version == 1 && !string.IsNullOrWhiteSpace(item.Key) && item.Key.Length <= 100 &&
        Enum.IsDefined(item.Size);
}
