using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Persists Haven's Agent and Prompt catalogues. Capabilities are persisted separately.</summary>
public sealed class CatalogRepository(ISqliteConnectionFactory factory) : ICatalogRepository
{
    public Task<IReadOnlyList<AgentDefinition>> GetAgentsAsync(CancellationToken cancellationToken) =>
        ReadAgentsAsync(includeDisabled: false, cancellationToken);

    public Task<IReadOnlyList<AgentDefinition>> GetAllAgentsAsync(CancellationToken cancellationToken) =>
        ReadAgentsAsync(includeDisabled: true, cancellationToken);

    private async Task<IReadOnlyList<AgentDefinition>> ReadAgentsAsync(bool includeDisabled, CancellationToken cancellationToken)
    {
        await SeedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = includeDisabled
            ? "SELECT * FROM agents ORDER BY is_built_in DESC,name;"
            : "SELECT * FROM agents WHERE is_enabled=1 ORDER BY is_built_in DESC,name;";
        var result = new List<AgentDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new AgentDefinition(
                reader.Guid("id"),
                reader.String("name"),
                reader.String("description"),
                reader.String("instructions"),
                reader.String("icon_key"),
                reader.String("preferred_model"),
                reader.NullableString("fallback_model"),
                reader.String("detection_rules"),
                reader.String("permissions_json"),
                reader.Boolean("is_built_in"),
                reader.Boolean("is_enabled"),
                reader.DateTimeOffset("updated_at")));
        }
        return result;
    }

    public async Task<IReadOnlyList<PromptDefinition>> GetPromptsAsync(CancellationToken cancellationToken)
    {
        await SeedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM prompts WHERE is_enabled=1 ORDER BY is_built_in DESC,name;";
        var result = new List<PromptDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new PromptDefinition(
                reader.Guid("id"),
                reader.String("name"),
                reader.String("description"),
                reader.String("icon_key"),
                reader.String("instructions"),
                reader.Boolean("persists"),
                reader.Boolean("is_built_in"),
                reader.Boolean("is_enabled"),
                reader.DateTimeOffset("updated_at"),
                reader.Boolean("is_agentic"),
                reader.String("allowed_modes_json")));
        }
        return result;
    }

    public async Task UpsertAgentAsync(AgentDefinition agent, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agents(id,name,description,instructions,icon_key,preferred_model,fallback_model,detection_rules,permissions_json,is_built_in,is_enabled,updated_at)
            VALUES($id,$name,$description,$instructions,$iconKey,$preferredModel,$fallbackModel,$detectionRules,$permissionsJson,$isBuiltIn,$isEnabled,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name,description=excluded.description,instructions=excluded.instructions,
              icon_key=excluded.icon_key,preferred_model=excluded.preferred_model,fallback_model=excluded.fallback_model,
              detection_rules=excluded.detection_rules,permissions_json=excluded.permissions_json,is_enabled=excluded.is_enabled,updated_at=excluded.updated_at;
            """;
        BindAgent(command, agent);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertPromptAsync(PromptDefinition prompt, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO prompts(id,name,description,icon_key,instructions,persists,is_built_in,is_enabled,updated_at,is_agentic,allowed_modes_json)
            VALUES($id,$name,$description,$iconKey,$instructions,$persists,$isBuiltIn,$isEnabled,$updatedAt,$isAgentic,$allowedModesJson)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name,description=excluded.description,icon_key=excluded.icon_key,
              instructions=excluded.instructions,persists=excluded.persists,is_enabled=excluded.is_enabled,updated_at=excluded.updated_at,
              is_agentic=excluded.is_agentic,allowed_modes_json=excluded.allowed_modes_json;
            """;
        BindPrompt(command, prompt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SetAgentEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken) =>
        SetEnabledAsync("agents", id, enabled, cancellationToken);

    public Task SetPromptEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken) =>
        SetEnabledAsync("prompts", id, enabled, cancellationToken);

    public Task DeleteCustomAgentAsync(Guid id, CancellationToken cancellationToken) =>
        DeleteCustomAsync("agents", id, cancellationToken);

    public Task DeleteCustomPromptAsync(Guid id, CancellationToken cancellationToken) =>
        DeleteCustomAsync("prompts", id, cancellationToken);

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        foreach (var agent in AgentCatalog.BuiltIns)
            await UpsertBuiltInAgentAsync(agent, cancellationToken).ConfigureAwait(false);
        foreach (var prompt in PromptCatalog.BuiltIns)
            await UpsertBuiltInPromptAsync(prompt, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertBuiltInAgentAsync(AgentDefinition agent, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agents(id,name,description,instructions,icon_key,preferred_model,fallback_model,detection_rules,permissions_json,is_built_in,is_enabled,updated_at)
            VALUES($id,$name,$description,$instructions,$iconKey,$preferredModel,$fallbackModel,$detectionRules,$permissionsJson,1,1,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET description=excluded.description,instructions=excluded.instructions,icon_key=excluded.icon_key,
              detection_rules=excluded.detection_rules,permissions_json=excluded.permissions_json,is_built_in=1;
            """;
        BindAgent(command, agent);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertBuiltInPromptAsync(PromptDefinition prompt, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO prompts(id,name,description,icon_key,instructions,persists,is_built_in,is_enabled,updated_at,is_agentic,allowed_modes_json)
            VALUES($id,$name,$description,$iconKey,$instructions,$persists,1,1,$updatedAt,$isAgentic,$allowedModesJson)
            ON CONFLICT(id) DO UPDATE SET description=excluded.description,icon_key=excluded.icon_key,instructions=excluded.instructions,
              persists=excluded.persists,is_agentic=excluded.is_agentic,allowed_modes_json=excluded.allowed_modes_json,
              is_built_in=1,is_enabled=1;
            """;
        BindPrompt(command, prompt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SetEnabledAsync(string table, Guid id, bool enabled, CancellationToken cancellationToken)
    {
        if (table is not ("agents" or "prompts")) throw new ArgumentOutOfRangeException(nameof(table));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {table} SET is_enabled=$enabled,updated_at=$updatedAt WHERE id=$id;";
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteCustomAsync(string table, Guid id, CancellationToken cancellationToken)
    {
        if (table is not ("agents" or "prompts")) throw new ArgumentOutOfRangeException(nameof(table));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE id=$id AND is_built_in=0;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void BindAgent(Microsoft.Data.Sqlite.SqliteCommand command, AgentDefinition agent)
    {
        command.Parameters.AddWithValue("$id", agent.Id.ToString());
        command.Parameters.AddWithValue("$name", agent.Name);
        command.Parameters.AddWithValue("$description", agent.Description);
        command.Parameters.AddWithValue("$instructions", agent.Instructions);
        command.Parameters.AddWithValue("$iconKey", agent.IconKey);
        command.Parameters.AddWithValue("$preferredModel", agent.PreferredModel);
        command.Parameters.AddWithValue("$fallbackModel", (object?)agent.FallbackModel ?? DBNull.Value);
        command.Parameters.AddWithValue("$detectionRules", agent.DetectionRules);
        command.Parameters.AddWithValue("$permissionsJson", agent.PermissionsJson);
        command.Parameters.AddWithValue("$isBuiltIn", agent.IsBuiltIn ? 1 : 0);
        command.Parameters.AddWithValue("$isEnabled", agent.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", agent.UpdatedAt.ToString("O"));
    }

    private static void BindPrompt(Microsoft.Data.Sqlite.SqliteCommand command, PromptDefinition prompt)
    {
        command.Parameters.AddWithValue("$id", prompt.Id.ToString());
        command.Parameters.AddWithValue("$name", prompt.Name);
        command.Parameters.AddWithValue("$description", prompt.Description);
        command.Parameters.AddWithValue("$iconKey", prompt.IconKey);
        command.Parameters.AddWithValue("$instructions", prompt.Instructions);
        command.Parameters.AddWithValue("$persists", prompt.Persists ? 1 : 0);
        command.Parameters.AddWithValue("$isBuiltIn", prompt.IsBuiltIn ? 1 : 0);
        command.Parameters.AddWithValue("$isEnabled", prompt.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", prompt.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$isAgentic", prompt.IsAgentic ? 1 : 0);
        command.Parameters.AddWithValue("$allowedModesJson", prompt.AllowedModesJson);
    }
}
