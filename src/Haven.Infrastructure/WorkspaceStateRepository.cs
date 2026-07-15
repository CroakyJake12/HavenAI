using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class WorkspaceStateRepository(ISqliteConnectionFactory factory) : IWorkspaceStateRepository
{
    public async Task<IReadOnlyList<MacroDefinition>> GetMacrosAsync(Guid? containerId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = containerId is null
            ? "SELECT * FROM macros WHERE is_enabled=1 ORDER BY name;"
            : "SELECT * FROM macros WHERE is_enabled=1 AND (container_id IS NULL OR container_id=$containerId) ORDER BY name;";
        if (containerId is not null) command.Parameters.AddWithValue("$containerId", containerId.Value.ToString());
        var result = new List<MacroDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new MacroDefinition(reader.Guid("id"), reader.String("name"), reader.String("description"), reader.String("instruction"),
                reader.NullableGuid("container_id"), reader.Boolean("is_enabled"), reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at")));
        return result;
    }

    public async Task UpsertMacroAsync(MacroDefinition macro, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO macros(id,name,description,instruction,container_id,is_enabled,created_at,updated_at)
            VALUES($id,$name,$description,$instruction,$containerId,$isEnabled,$createdAt,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name,description=excluded.description,instruction=excluded.instruction,
              container_id=excluded.container_id,is_enabled=excluded.is_enabled,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", macro.Id.ToString());
        command.Parameters.AddWithValue("$name", macro.Name);
        command.Parameters.AddWithValue("$description", macro.Description);
        command.Parameters.AddWithValue("$instruction", macro.Instruction);
        command.Parameters.AddWithValue("$containerId", (object?)macro.ContainerId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$isEnabled", macro.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", macro.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", macro.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteMacroAsync(Guid id, CancellationToken cancellationToken)
    {
        await ExecuteDeleteAsync("macros", id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WorkspaceVersion>> GetVersionsAsync(Guid? containerId, string? relativePath, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var clauses = new List<string>();
        if (containerId is not null)
        {
            clauses.Add("container_id=$containerId");
            command.Parameters.AddWithValue("$containerId", containerId.Value.ToString());
        }
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            clauses.Add("relative_path=$relativePath");
            command.Parameters.AddWithValue("$relativePath", relativePath);
        }
        command.CommandText = $"SELECT * FROM workspace_versions{(clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses))} ORDER BY created_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        var result = new List<WorkspaceVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new WorkspaceVersion(reader.Guid("id"), reader.NullableGuid("conversation_id"), reader.NullableGuid("container_id"),
                reader.String("workspace_root"), reader.String("relative_path"), (WorkspaceVersionKind)reader.Int32("kind"),
                reader.String("before_content"), reader.String("after_content"), reader.String("summary"), reader.Int32("lines_added"),
                reader.Int32("lines_removed"), reader.DateTimeOffset("created_at")));
        return result;
    }

    public async Task AddVersionAsync(WorkspaceVersion version, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workspace_versions(id,conversation_id,container_id,workspace_root,relative_path,kind,before_content,after_content,summary,lines_added,lines_removed,created_at)
            VALUES($id,$conversationId,$containerId,$workspaceRoot,$relativePath,$kind,$beforeContent,$afterContent,$summary,$linesAdded,$linesRemoved,$createdAt);
            """;
        command.Parameters.AddWithValue("$id", version.Id.ToString());
        command.Parameters.AddWithValue("$conversationId", (object?)version.ConversationId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$containerId", (object?)version.ContainerId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$workspaceRoot", version.WorkspaceRoot);
        command.Parameters.AddWithValue("$relativePath", version.RelativePath);
        command.Parameters.AddWithValue("$kind", (int)version.Kind);
        command.Parameters.AddWithValue("$beforeContent", version.BeforeContent);
        command.Parameters.AddWithValue("$afterContent", version.AfterContent);
        command.Parameters.AddWithValue("$summary", version.Summary);
        command.Parameters.AddWithValue("$linesAdded", version.LinesAdded);
        command.Parameters.AddWithValue("$linesRemoved", version.LinesRemoved);
        command.Parameters.AddWithValue("$createdAt", version.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DecisionRecord>> GetDecisionsAsync(Guid containerId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM decisions WHERE container_id=$containerId ORDER BY updated_at DESC;";
        command.Parameters.AddWithValue("$containerId", containerId.ToString());
        var result = new List<DecisionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new DecisionRecord(reader.Guid("id"), reader.Guid("container_id"), reader.String("title"), reader.String("decision_text"),
                reader.String("alternatives"), reader.String("reasoning"), reader.String("evidence"), reader.String("consequences"),
                reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at")));
        return result;
    }

    public async Task UpsertDecisionAsync(DecisionRecord decision, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO decisions(id,container_id,title,decision_text,alternatives,reasoning,evidence,consequences,created_at,updated_at)
            VALUES($id,$containerId,$title,$decision,$alternatives,$reasoning,$evidence,$consequences,$createdAt,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET title=excluded.title,decision_text=excluded.decision_text,alternatives=excluded.alternatives,
              reasoning=excluded.reasoning,evidence=excluded.evidence,consequences=excluded.consequences,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", decision.Id.ToString());
        command.Parameters.AddWithValue("$containerId", decision.ContainerId.ToString());
        command.Parameters.AddWithValue("$title", decision.Title);
        command.Parameters.AddWithValue("$decision", decision.Decision);
        command.Parameters.AddWithValue("$alternatives", decision.Alternatives);
        command.Parameters.AddWithValue("$reasoning", decision.Reasoning);
        command.Parameters.AddWithValue("$evidence", decision.Evidence);
        command.Parameters.AddWithValue("$consequences", decision.Consequences);
        command.Parameters.AddWithValue("$createdAt", decision.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", decision.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteDecisionAsync(Guid id, CancellationToken cancellationToken) => ExecuteDeleteAsync("decisions", id, cancellationToken);

    private async Task ExecuteDeleteAsync(string table, Guid id, CancellationToken cancellationToken)
    {
        if (table is not ("macros" or "decisions")) throw new ArgumentOutOfRangeException(nameof(table));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
