using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class DashboardLayoutRepository(ISqliteConnectionFactory factory) : IDashboardLayoutRepository
{
    private const string SettingKey = "dashboard.layout.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    private static bool IsValid(DashboardTileLayout item) =>
        item.Version == 1 && !string.IsNullOrWhiteSpace(item.Key) && item.Key.Length <= 100 &&
        Enum.IsDefined(item.Size);
}
