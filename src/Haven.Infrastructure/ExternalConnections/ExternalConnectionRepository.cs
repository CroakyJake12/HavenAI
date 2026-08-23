using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed class ExternalConnectionRepository(ISqliteConnectionFactory factory) : IExternalConnectionRepository
{
    public async Task<IReadOnlyList<ExternalConnection>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM external_connections ORDER BY name,updated_at DESC;";
        var result = new List<ExternalConnection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    public async Task<ExternalConnection?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM external_connections WHERE id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task UpsertAsync(ExternalConnection item, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO external_connections(id,name,provider_key,kind,preset_key,is_enabled,state,status,configuration_json,server_name,server_version,protocol_version,created_at,updated_at)
            VALUES($id,$name,$provider,$kind,$preset,$enabled,$state,$status,$config,$serverName,$serverVersion,$protocol,$created,$updated)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name,provider_key=excluded.provider_key,kind=excluded.kind,preset_key=excluded.preset_key,
            is_enabled=excluded.is_enabled,state=excluded.state,status=excluded.status,configuration_json=excluded.configuration_json,
            server_name=excluded.server_name,server_version=excluded.server_version,protocol_version=excluded.protocol_version,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$name", item.Name);
        command.Parameters.AddWithValue("$provider", item.ProviderKey);
        command.Parameters.AddWithValue("$kind", (int)item.Kind);
        command.Parameters.AddWithValue("$preset", item.PresetKey);
        command.Parameters.AddWithValue("$enabled", item.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$state", (int)item.State);
        command.Parameters.AddWithValue("$status", item.Status);
        command.Parameters.AddWithValue("$config", item.ConfigurationJson);
        command.Parameters.AddWithValue("$serverName", (object?)item.ServerName ?? DBNull.Value);
        command.Parameters.AddWithValue("$serverVersion", (object?)item.ServerVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$protocol", (object?)item.ProtocolVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", item.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM external_connections WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ExternalConnection Read(SqliteDataReader reader)
    {
        string? Nullable(string name) { var i = reader.GetOrdinal(name); return reader.IsDBNull(i) ? null : reader.GetString(i); }
        return new ExternalConnection(
            Guid.Parse(reader.GetString(reader.GetOrdinal("id"))), reader.GetString(reader.GetOrdinal("name")), reader.GetString(reader.GetOrdinal("provider_key")),
            (ExternalConnectionKind)reader.GetInt32(reader.GetOrdinal("kind")), reader.GetString(reader.GetOrdinal("preset_key")), reader.GetInt32(reader.GetOrdinal("is_enabled")) != 0,
            (ExternalConnectionState)reader.GetInt32(reader.GetOrdinal("state")), reader.GetString(reader.GetOrdinal("status")), reader.GetString(reader.GetOrdinal("configuration_json")),
            Nullable("server_name"), Nullable("server_version"), Nullable("protocol_version"),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at")), System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at")), System.Globalization.CultureInfo.InvariantCulture));
    }
}
