using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class ModeUsageRepository(ISqliteConnectionFactory factory) : IModeUsageRepository
{
    public async Task RecordUsageAsync(Guid modeId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mode_usage(id, mode_id, date, turn_count, completion_count, total_duration_ms)
            VALUES($id, $modeId, $date, 1, 0, 0)
            ON CONFLICT(mode_id, date) DO UPDATE SET turn_count=turn_count+1;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$modeId", modeId.ToString());
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ModeUsage>> GetRecentUsageAsync(int days, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM mode_usage WHERE date >= date('now', '-' || $days || ' days') ORDER BY date DESC;";
        command.Parameters.AddWithValue("$days", days);
        var results = new List<ModeUsage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(ReadUsage(reader));
        return results;
    }

    public async Task<int> GetTotalUseCountAsync(Guid modeId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(SUM(turn_count), 0) FROM mode_usage WHERE mode_id=$modeId;";
        command.Parameters.AddWithValue("$modeId", modeId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<ModeUsage>> GetUsageByModeAsync(Guid modeId, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM mode_usage WHERE mode_id=$modeId ORDER BY date DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$modeId", modeId.ToString());
        command.Parameters.AddWithValue("$limit", limit);
        var results = new List<ModeUsage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(ReadUsage(reader));
        return results;
    }

    private static ModeUsage ReadUsage(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        Guid.Parse(reader.String("id")),
        Guid.Parse(reader.String("mode_id")),
        DateOnly.Parse(reader.String("date")),
        reader.Int32("turn_count"),
        reader.Int32("completion_count"),
        TimeSpan.FromMilliseconds(reader.GetInt64(reader.GetOrdinal("total_duration_ms"))));
}
