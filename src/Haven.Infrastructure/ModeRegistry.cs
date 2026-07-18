/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/ModeRegistry.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ModeRegistry. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents mode registry and keeps its related state and behavior together.
/// </summary>
public sealed class ModeRegistry(ISqliteConnectionFactory factory) : IModeRegistry
{
    /// <summary>
    /// Retrieves modes async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM mode_definitions ORDER BY source, name;";
        var results = new List<ModeDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(ReadMode(reader));
        return results;
    }

    /// <summary>
    /// Retrieves mode by key async for the current operation.
    /// </summary>
    public async Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM mode_definitions WHERE key=$key;";
        command.Parameters.AddWithValue("$key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadMode(reader) : null;
    }

    /// <summary>
    /// Retrieves mode by id async for the current operation.
    /// </summary>
    public async Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM mode_definitions WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadMode(reader) : null;
    }

    /// <summary>
    /// Performs upsert mode asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertModeAsync(ModeDefinition mode, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mode_definitions(id, key, name, description, icon_key, base_mode, surfaces_json,
                tool_allowlist_json, tool_denylist_json, plugins_json, system_prompt_suffix, source,
                install_state, author, version, tags_json, created_at, updated_at, is_enabled)
            VALUES($id, $key, $name, $description, $iconKey, $baseMode, $surfacesJson,
                $toolAllowlistJson, $toolDenylistJson, $pluginsJson, $systemPromptSuffix, $source,
                $installState, $author, $version, $tagsJson, $createdAt, $updatedAt, $isEnabled)
            ON CONFLICT(id) DO UPDATE SET
                name=$name, description=$description, icon_key=$iconKey, base_mode=$baseMode,
                surfaces_json=$surfacesJson, tool_allowlist_json=$toolAllowlistJson,
                tool_denylist_json=$toolDenylistJson, plugins_json=$pluginsJson,
                system_prompt_suffix=$systemPromptSuffix, source=$source, install_state=$installState,
                author=$author, version=$version, tags_json=$tagsJson, updated_at=$updatedAt,
                is_enabled=$isEnabled;
            """;
        command.Parameters.AddWithValue("$id", mode.Id.ToString());
        command.Parameters.AddWithValue("$key", mode.Key);
        command.Parameters.AddWithValue("$name", mode.Name);
        command.Parameters.AddWithValue("$description", mode.Description);
        command.Parameters.AddWithValue("$iconKey", mode.IconKey);
        command.Parameters.AddWithValue("$baseMode", (int)mode.BaseMode);
        command.Parameters.AddWithValue("$surfacesJson", mode.SurfacesJson);
        command.Parameters.AddWithValue("$toolAllowlistJson", mode.ToolAllowlistJson);
        command.Parameters.AddWithValue("$toolDenylistJson", mode.ToolDenylistJson);
        command.Parameters.AddWithValue("$pluginsJson", mode.PluginsJson);
        command.Parameters.AddWithValue("$systemPromptSuffix", mode.SystemPromptSuffix);
        command.Parameters.AddWithValue("$source", (int)mode.Source);
        command.Parameters.AddWithValue("$installState", (int)mode.InstallState);
        command.Parameters.AddWithValue("$author", mode.Author);
        command.Parameters.AddWithValue("$version", mode.Version);
        command.Parameters.AddWithValue("$tagsJson", mode.TagsJson);
        command.Parameters.AddWithValue("$createdAt", mode.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", mode.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$isEnabled", mode.IsEnabled ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves versions async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid modeId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM mode_versions WHERE mode_id=$modeId ORDER BY major DESC, minor DESC, patch DESC;";
        command.Parameters.AddWithValue("$modeId", modeId.ToString());
        var results = new List<ModeVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(new ModeVersion(
                Guid.Parse(reader.String("id")),
                Guid.Parse(reader.String("mode_id")),
                reader.Int32("major"),
                reader.Int32("minor"),
                reader.Int32("patch"),
                reader.String("manifest_json"),
                reader.String("changelog"),
                reader.DateTimeOffset("published_at")));
        return results;
    }

    /// <summary>
    /// Performs add version asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task AddVersionAsync(ModeVersion version, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mode_versions(id, mode_id, major, minor, patch, manifest_json, changelog, published_at)
            VALUES($id, $modeId, $major, $minor, $patch, $manifestJson, $changelog, $publishedAt);
            """;
        command.Parameters.AddWithValue("$id", version.Id.ToString());
        command.Parameters.AddWithValue("$modeId", version.ModeId.ToString());
        command.Parameters.AddWithValue("$major", version.Major);
        command.Parameters.AddWithValue("$minor", version.Minor);
        command.Parameters.AddWithValue("$patch", version.Patch);
        command.Parameters.AddWithValue("$manifestJson", version.ManifestJson);
        command.Parameters.AddWithValue("$changelog", version.Changelog);
        command.Parameters.AddWithValue("$publishedAt", version.PublishedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves grants async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid modeId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM mode_permission_grants WHERE mode_id=$modeId;";
        command.Parameters.AddWithValue("$modeId", modeId.ToString());
        var results = new List<ModePermissionGrant>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(new ModePermissionGrant(
                Guid.Parse(reader.String("id")),
                Guid.Parse(reader.String("mode_id")),
                (PermissionMode)reader.Int32("file_permission"),
                (PermissionMode)reader.Int32("command_permission"),
                (PermissionMode)reader.Int32("browser_permission"),
                reader.Boolean("allow_desktop_tools"),
                reader.Boolean("allow_file_system_writes"),
                reader.DateTimeOffset("granted_at")));
        return results;
    }

    /// <summary>
    /// Performs upsert grant asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mode_permission_grants(id, mode_id, file_permission, command_permission,
                browser_permission, allow_desktop_tools, allow_file_system_writes, granted_at)
            VALUES($id, $modeId, $filePermission, $commandPermission,
                $browserPermission, $allowDesktopTools, $allowFileSystemWrites, $grantedAt)
            ON CONFLICT(id) DO UPDATE SET
                file_permission=$filePermission, command_permission=$commandPermission,
                browser_permission=$browserPermission, allow_desktop_tools=$allowDesktopTools,
                allow_file_system_writes=$allowFileSystemWrites, granted_at=$grantedAt;
            """;
        command.Parameters.AddWithValue("$id", grant.Id.ToString());
        command.Parameters.AddWithValue("$modeId", grant.ModeId.ToString());
        command.Parameters.AddWithValue("$filePermission", (int)grant.FilePermission);
        command.Parameters.AddWithValue("$commandPermission", (int)grant.CommandPermission);
        command.Parameters.AddWithValue("$browserPermission", (int)grant.BrowserPermission);
        command.Parameters.AddWithValue("$allowDesktopTools", grant.AllowDesktopTools ? 1 : 0);
        command.Parameters.AddWithValue("$allowFileSystemWrites", grant.AllowFileSystemWrites ? 1 : 0);
        command.Parameters.AddWithValue("$grantedAt", grant.GrantedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the read mode step owned by this component.
    /// </summary>
    private static ModeDefinition ReadMode(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        Guid.Parse(reader.String("id")),
        reader.String("key"),
        reader.String("name"),
        reader.String("description"),
        reader.String("icon_key"),
        (HavenMode)reader.Int32("base_mode"),
        reader.String("surfaces_json"),
        reader.String("tool_allowlist_json"),
        reader.String("tool_denylist_json"),
        reader.String("plugins_json"),
        reader.String("system_prompt_suffix"),
        (ModeSource)reader.Int32("source"),
        (ModeInstallState)reader.Int32("install_state"),
        reader.String("author"),
        reader.String("version"),
        reader.String("tags_json"),
        reader.DateTimeOffset("created_at"),
        reader.DateTimeOffset("updated_at"),
        reader.Boolean("is_enabled"));
}
