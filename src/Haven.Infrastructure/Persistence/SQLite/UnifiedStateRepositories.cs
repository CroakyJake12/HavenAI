using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class HavenNotificationRepository(ISqliteConnectionFactory factory) : IHavenNotificationRepository
{
    public async Task UpsertAsync(HavenNotification notification, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO haven_notifications(id,kind,priority,is_live,is_read,is_dismissed,requires_attention,coalescing_key,payload_json,created_at,updated_at)
            VALUES($id,$kind,$priority,$isLive,$isRead,$isDismissed,$attention,$key,$payload,$createdAt,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET kind=excluded.kind,priority=excluded.priority,is_live=excluded.is_live,is_read=excluded.is_read,
              is_dismissed=excluded.is_dismissed,requires_attention=excluded.requires_attention,coalescing_key=excluded.coalescing_key,
              payload_json=excluded.payload_json,updated_at=excluded.updated_at;
            """;
        Bind(command, notification);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HavenNotification>> GetRecentAsync(int limit, bool includeDismissed, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM haven_notifications WHERE ($include=1 OR is_dismissed=0) ORDER BY requires_attention DESC,updated_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$include", includeDismissed ? 1 : 0);
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<HavenNotification>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(UnifiedPersistenceJson.Read<HavenNotification>(reader.GetString(0)));
        return result;
    }

    public Task SetReadAsync(Guid id, bool isRead, CancellationToken cancellationToken) => MutateAsync(id, "is_read", isRead, cancellationToken);
    public Task DismissAsync(Guid id, CancellationToken cancellationToken) => MutateAsync(id, "is_dismissed", true, cancellationToken);

    private async Task MutateAsync(Guid id, string column, bool value, CancellationToken cancellationToken)
    {
        if (column is not ("is_read" or "is_dismissed")) throw new ArgumentOutOfRangeException(nameof(column));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var read = connection.CreateCommand();
        read.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        read.CommandText = "SELECT payload_json FROM haven_notifications WHERE id=$id;";
        read.Parameters.AddWithValue("$id", id.ToString());
        var payload = await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (payload is null) return;
        var item = UnifiedPersistenceJson.Read<HavenNotification>(payload);
        item = column == "is_read" ? item with { IsRead = value, UpdatedAt = DateTimeOffset.UtcNow }
            : item with { IsDismissed = value, UpdatedAt = DateTimeOffset.UtcNow };
        await using var update = connection.CreateCommand();
        update.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        update.CommandText = $"UPDATE haven_notifications SET {column}=$value,payload_json=$payload,updated_at=$updatedAt WHERE id=$id;";
        update.Parameters.AddWithValue("$value", value ? 1 : 0);
        update.Parameters.AddWithValue("$payload", UnifiedPersistenceJson.Write(item));
        update.Parameters.AddWithValue("$updatedAt", item.UpdatedAt.ToString("O"));
        update.Parameters.AddWithValue("$id", id.ToString());
        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Bind(Microsoft.Data.Sqlite.SqliteCommand command, HavenNotification item)
    {
        command.Parameters.AddWithValue("$id", item.Id.ToString()); command.Parameters.AddWithValue("$kind", (int)item.Kind);
        command.Parameters.AddWithValue("$priority", (int)item.Priority); command.Parameters.AddWithValue("$isLive", item.IsLive ? 1 : 0);
        command.Parameters.AddWithValue("$isRead", item.IsRead ? 1 : 0); command.Parameters.AddWithValue("$isDismissed", item.IsDismissed ? 1 : 0);
        command.Parameters.AddWithValue("$attention", item.RequiresAttention ? 1 : 0); command.Parameters.AddWithValue("$key", (object?)item.CoalescingKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$payload", UnifiedPersistenceJson.Write(item)); command.Parameters.AddWithValue("$createdAt", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", item.UpdatedAt.ToString("O"));
    }
}

public sealed class WorkspaceSessionRepository(ISqliteConnectionFactory factory) : IWorkspaceSessionRepository
{
    public async Task<WorkspaceSessionSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM workspace_session WHERE id=1;";
        var payload = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return payload is null ? null : UnifiedPersistenceJson.Read<WorkspaceSessionSnapshot>(payload);
    }

    public async Task SaveAsync(WorkspaceSessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workspace_session(id,schema_version,payload_json,saved_at) VALUES(1,$version,$payload,$savedAt)
            ON CONFLICT(id) DO UPDATE SET schema_version=excluded.schema_version,payload_json=excluded.payload_json,saved_at=excluded.saved_at;
            """;
        command.Parameters.AddWithValue("$version", snapshot.SchemaVersion);
        command.Parameters.AddWithValue("$payload", UnifiedPersistenceJson.Write(snapshot));
        command.Parameters.AddWithValue("$savedAt", snapshot.SavedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ExtensionRepository(ISqliteConnectionFactory factory) : IExtensionRepository
{
    public async Task<IReadOnlyList<ExtensionSource>> GetSourcesAsync(CancellationToken cancellationToken) =>
        await ReadAsync<ExtensionSource>("SELECT payload_json FROM extension_sources ORDER BY updated_at DESC;", cancellationToken).ConfigureAwait(false);

    public async Task UpsertSourceAsync(ExtensionSource source, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO extension_sources(id,type,repository_uri,is_private,is_enabled,payload_json,updated_at)
            VALUES($id,$type,$uri,$private,$enabled,$payload,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET type=excluded.type,repository_uri=excluded.repository_uri,is_private=excluded.is_private,
              is_enabled=excluded.is_enabled,payload_json=excluded.payload_json,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", source.Id.ToString()); command.Parameters.AddWithValue("$type", (int)source.Type);
        command.Parameters.AddWithValue("$uri", source.RepositoryUri); command.Parameters.AddWithValue("$private", source.IsPrivate ? 1 : 0);
        command.Parameters.AddWithValue("$enabled", source.IsEnabled ? 1 : 0); command.Parameters.AddWithValue("$payload", UnifiedPersistenceJson.Write(source));
        command.Parameters.AddWithValue("$updatedAt", (source.LastRefreshedAt ?? DateTimeOffset.UtcNow).ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteSourceAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM extension_sources WHERE id=$id AND NOT EXISTS(SELECT 1 FROM extension_packages WHERE source_id=$id);";
        command.Parameters.AddWithValue("$id", sourceId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InstalledExtensionPackage>> GetInstalledAsync(CancellationToken cancellationToken) =>
        await ReadAsync<InstalledExtensionPackage>("SELECT payload_json FROM extension_packages ORDER BY updated_at DESC;", cancellationToken).ConfigureAwait(false);

    public async Task UpsertInstalledAsync(InstalledExtensionPackage package, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO extension_packages(id,package_id,source_id,package_type,version,state,is_enabled,granted_permissions,content_hash,payload_json,installed_at,updated_at)
            VALUES($id,$packageId,$sourceId,$type,$version,$state,$enabled,$permissions,$hash,$payload,$installedAt,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET package_id=excluded.package_id,source_id=excluded.source_id,package_type=excluded.package_type,
              version=excluded.version,state=excluded.state,is_enabled=excluded.is_enabled,granted_permissions=excluded.granted_permissions,
              content_hash=excluded.content_hash,payload_json=excluded.payload_json,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", package.Id.ToString()); command.Parameters.AddWithValue("$packageId", package.Manifest.PackageId);
        command.Parameters.AddWithValue("$sourceId", package.SourceId.ToString()); command.Parameters.AddWithValue("$type", (int)package.Manifest.PackageType);
        command.Parameters.AddWithValue("$version", package.Manifest.Version); command.Parameters.AddWithValue("$state", (int)package.State);
        command.Parameters.AddWithValue("$enabled", package.IsEnabled ? 1 : 0); command.Parameters.AddWithValue("$permissions", (int)package.GrantedPermissions);
        command.Parameters.AddWithValue("$hash", package.ContentHash); command.Parameters.AddWithValue("$payload", UnifiedPersistenceJson.Write(package));
        command.Parameters.AddWithValue("$installedAt", package.InstalledAt.ToString("O")); command.Parameters.AddWithValue("$updatedAt", package.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteInstalledAsync(Guid packageId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM extension_packages WHERE id=$id;";
        command.Parameters.AddWithValue("$id", packageId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> ReadAsync<T>(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        var result = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(UnifiedPersistenceJson.Read<T>(reader.GetString(0)));
        return result;
    }
}
