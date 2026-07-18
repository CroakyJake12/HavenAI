/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/PinRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns PinRepository. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents pin repository and keeps its related state and behavior together.
/// </summary>
public sealed class PinRepository(ISqliteConnectionFactory factory) : IPinRepository
{
    /// <summary>
    /// Retrieves pins async for the current operation.
    /// </summary>
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

    /// <summary>
    /// Performs upsert pin asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs delete pin asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeletePinAsync(Guid modeId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM mode_pins WHERE mode_id=$modeId;";
        command.Parameters.AddWithValue("$modeId", modeId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
