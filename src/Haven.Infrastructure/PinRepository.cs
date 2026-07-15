using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class PinRepository(ISqliteConnectionFactory factory) : IPinRepository
{
    public async Task<IReadOnlyList<ModePin>> GetPinsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM mode_pins ORDER BY sort_order;";
        var results = new List<ModePin>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(new ModePin(
                Guid.Parse(reader.String("id")),
                Guid.Parse(reader.String("mode_id")),
                reader.Int32("sort_order"),
                reader.DateTimeOffset("pinned_at")));
        return results;
    }

    public async Task UpsertPinAsync(ModePin pin, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mode_pins(id, mode_id, sort_order, pinned_at)
            VALUES($id, $modeId, $sortOrder, $pinnedAt)
            ON CONFLICT(mode_id) DO UPDATE SET sort_order=$sortOrder, pinned_at=$pinnedAt;
            """;
        command.Parameters.AddWithValue("$id", pin.Id.ToString());
        command.Parameters.AddWithValue("$modeId", pin.ModeId.ToString());
        command.Parameters.AddWithValue("$sortOrder", pin.SortOrder);
        command.Parameters.AddWithValue("$pinnedAt", pin.PinnedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeletePinAsync(Guid modeId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM mode_pins WHERE mode_id=$modeId;";
        command.Parameters.AddWithValue("$modeId", modeId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
