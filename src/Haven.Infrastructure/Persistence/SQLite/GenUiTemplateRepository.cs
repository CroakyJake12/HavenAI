using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class GenUiTemplateRepository(ISqliteConnectionFactory factory) : IGenUiTemplateRepository
{
    private int _seeded;

    public async Task<GenUiTemplateDefinition?> GetByKeyAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await SeedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM genui_templates WHERE key=$key AND is_enabled=1 LIMIT 1;";
        command.Parameters.AddWithValue("$key", key.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<GenUiTemplateDefinition>> SearchAsync(
        string query,
        CapabilityPlatform platform,
        string? compatibleAppKey,
        int limit,
        CancellationToken cancellationToken)
    {
        if (platform is CapabilityPlatform.None or CapabilityPlatform.All)
            throw new ArgumentOutOfRangeException(nameof(platform), "Select one current host platform.");
        limit = Math.Clamp(limit, 1, 100);
        await SeedAsync(cancellationToken).ConfigureAwait(false);
        var term = query?.Trim() ?? string.Empty;
        var app = compatibleAppKey?.Trim() ?? string.Empty;

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM genui_templates
            WHERE is_enabled=1
              AND (platforms & $platform) != 0
              AND ($query='' OR lower(name) LIKE $like OR lower(description) LIKE $like
                   OR lower(category) LIKE $like OR lower(tags_json) LIKE $like)
              AND ($app='' OR lower(compatible_apps_json) LIKE $appLike)
            ORDER BY maturity DESC,is_built_in DESC,name
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$platform", (int)platform);
        command.Parameters.AddWithValue("$query", term.ToLowerInvariant());
        command.Parameters.AddWithValue("$like", "%" + term.ToLowerInvariant() + "%");
        command.Parameters.AddWithValue("$app", app.ToLowerInvariant());
        command.Parameters.AddWithValue("$appLike", "%\"" + app.ToLowerInvariant() + "\"%");
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<GenUiTemplateDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    public async Task UpsertAsync(GenUiTemplateDefinition template, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = UpsertSql;
        Bind(command, template);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteCustomAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM genui_templates WHERE id=$id AND is_built_in=0;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _seeded, 1) != 0) return;
        try
        {
            foreach (var template in TemplateRegistryCatalog.BuiltIns)
                await UpsertBuiltInAsync(template, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Exchange(ref _seeded, 0);
            throw;
        }
    }

    private async Task UpsertBuiltInAsync(GenUiTemplateDefinition template, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = UpsertSql.Replace(
            "is_built_in=excluded.is_built_in,is_enabled=excluded.is_enabled",
            "is_built_in=1,is_enabled=1",
            StringComparison.Ordinal);
        Bind(command, template with { IsBuiltIn = true, IsEnabled = true });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static GenUiTemplateDefinition Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        reader.Guid("id"), reader.String("key"), reader.String("version"), reader.String("name"),
        reader.String("description"), reader.String("category"), Strings(reader.String("tags_json")),
        reader.String("canonical_implementation"), (GenUiTemplateScale)reader.Int32("scale"),
        Strings(reader.String("recommended_apps_json")), Strings(reader.String("compatible_apps_json")),
        Strings(reader.String("inputs_json")), Strings(reader.String("outputs_json")),
        Enums<GenUiEventType>(reader.String("emitted_events_json")),
        Strings(reader.String("configurable_properties_json")), Strings(reader.String("data_requirements_json")),
        Strings(reader.String("supported_interactions_json")), Strings(reader.String("havenui_primitives_json")),
        Strings(reader.String("app_services_json")), Strings(reader.String("capabilities_json")),
        Enums<ToolCapability>(reader.String("model_capabilities_json")), reader.Boolean("requires_network"),
        reader.Boolean("supports_offline"), (CapabilityPlatform)reader.Int32("platforms"),
        reader.String("accessibility_summary"), reader.Boolean("supports_persistence"),
        reader.Boolean("supports_thread_scope"), reader.Boolean("supports_user_apps"),
        reader.Boolean("supports_mini_apps"), reader.Boolean("supports_embedding"),
        (GenUiAgentInteractionMode)reader.Int32("agent_interaction"), reader.Boolean("deterministic_without_model"),
        (GenUiStateOwnership)reader.Int32("state_ownership"), (GenUiTemplateMaturity)reader.Int32("maturity"),
        reader.Boolean("is_built_in"), reader.Boolean("is_enabled"), reader.DateTimeOffset("updated_at"));

    private static void Bind(Microsoft.Data.Sqlite.SqliteCommand command, GenUiTemplateDefinition item)
    {
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$key", item.Key);
        command.Parameters.AddWithValue("$version", item.Version);
        command.Parameters.AddWithValue("$name", item.Name);
        command.Parameters.AddWithValue("$description", item.Description);
        command.Parameters.AddWithValue("$category", item.Category);
        command.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(item.Tags));
        command.Parameters.AddWithValue("$implementation", item.CanonicalImplementation);
        command.Parameters.AddWithValue("$scale", (int)item.Scale);
        command.Parameters.AddWithValue("$recommendedApps", JsonSerializer.Serialize(item.RecommendedApps));
        command.Parameters.AddWithValue("$compatibleApps", JsonSerializer.Serialize(item.CompatibleApps));
        command.Parameters.AddWithValue("$inputs", JsonSerializer.Serialize(item.Inputs));
        command.Parameters.AddWithValue("$outputs", JsonSerializer.Serialize(item.Outputs));
        command.Parameters.AddWithValue("$events", JsonSerializer.Serialize(item.EmittedEvents.Select(value => value.ToString())));
        command.Parameters.AddWithValue("$properties", JsonSerializer.Serialize(item.ConfigurableProperties));
        command.Parameters.AddWithValue("$requirements", JsonSerializer.Serialize(item.DataRequirements));
        command.Parameters.AddWithValue("$interactions", JsonSerializer.Serialize(item.SupportedInteractions));
        command.Parameters.AddWithValue("$primitives", JsonSerializer.Serialize(item.RequiredHavenUiPrimitives));
        command.Parameters.AddWithValue("$services", JsonSerializer.Serialize(item.RequiredAppServices));
        command.Parameters.AddWithValue("$capabilities", JsonSerializer.Serialize(item.RequiredCapabilities));
        command.Parameters.AddWithValue("$modelCapabilities", JsonSerializer.Serialize(item.RequiredModelCapabilities.Select(value => value.ToString())));
        command.Parameters.AddWithValue("$network", item.RequiresNetwork ? 1 : 0);
        command.Parameters.AddWithValue("$offline", item.SupportsOffline ? 1 : 0);
        command.Parameters.AddWithValue("$platforms", (int)item.Platforms);
        command.Parameters.AddWithValue("$accessibility", item.AccessibilitySummary);
        command.Parameters.AddWithValue("$persistence", item.SupportsPersistence ? 1 : 0);
        command.Parameters.AddWithValue("$thread", item.SupportsThreadScope ? 1 : 0);
        command.Parameters.AddWithValue("$userApps", item.SupportsUserApps ? 1 : 0);
        command.Parameters.AddWithValue("$miniApps", item.SupportsMiniApps ? 1 : 0);
        command.Parameters.AddWithValue("$embedding", item.SupportsEmbedding ? 1 : 0);
        command.Parameters.AddWithValue("$agent", (int)item.AgentInteraction);
        command.Parameters.AddWithValue("$deterministic", item.IsDeterministicWithoutModel ? 1 : 0);
        command.Parameters.AddWithValue("$ownership", (int)item.StateOwnership);
        command.Parameters.AddWithValue("$maturity", (int)item.Maturity);
        command.Parameters.AddWithValue("$builtIn", item.IsBuiltIn ? 1 : 0);
        command.Parameters.AddWithValue("$enabled", item.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", item.UpdatedAt.ToString("O"));
    }

    private static IReadOnlyList<string> Strings(string json) =>
        JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static IReadOnlyList<T> Enums<T>(string json) where T : struct, Enum =>
        Strings(json).Select(value => Enum.Parse<T>(value, ignoreCase: true)).ToArray();

    private const string UpsertSql = """
        INSERT INTO genui_templates(
          id,key,version,name,description,category,tags_json,canonical_implementation,scale,
          recommended_apps_json,compatible_apps_json,inputs_json,outputs_json,emitted_events_json,
          configurable_properties_json,data_requirements_json,supported_interactions_json,havenui_primitives_json,
          app_services_json,capabilities_json,model_capabilities_json,requires_network,supports_offline,platforms,
          accessibility_summary,supports_persistence,supports_thread_scope,supports_user_apps,supports_mini_apps,
          supports_embedding,agent_interaction,deterministic_without_model,state_ownership,maturity,is_built_in,is_enabled,updated_at)
        VALUES(
          $id,$key,$version,$name,$description,$category,$tags,$implementation,$scale,
          $recommendedApps,$compatibleApps,$inputs,$outputs,$events,$properties,$requirements,$interactions,$primitives,
          $services,$capabilities,$modelCapabilities,$network,$offline,$platforms,$accessibility,$persistence,$thread,
          $userApps,$miniApps,$embedding,$agent,$deterministic,$ownership,$maturity,$builtIn,$enabled,$updatedAt)
        ON CONFLICT(id) DO UPDATE SET
          key=excluded.key,version=excluded.version,name=excluded.name,description=excluded.description,
          category=excluded.category,tags_json=excluded.tags_json,canonical_implementation=excluded.canonical_implementation,
          scale=excluded.scale,recommended_apps_json=excluded.recommended_apps_json,compatible_apps_json=excluded.compatible_apps_json,
          inputs_json=excluded.inputs_json,outputs_json=excluded.outputs_json,emitted_events_json=excluded.emitted_events_json,
          configurable_properties_json=excluded.configurable_properties_json,data_requirements_json=excluded.data_requirements_json,
          supported_interactions_json=excluded.supported_interactions_json,havenui_primitives_json=excluded.havenui_primitives_json,
          app_services_json=excluded.app_services_json,capabilities_json=excluded.capabilities_json,
          model_capabilities_json=excluded.model_capabilities_json,requires_network=excluded.requires_network,
          supports_offline=excluded.supports_offline,platforms=excluded.platforms,accessibility_summary=excluded.accessibility_summary,
          supports_persistence=excluded.supports_persistence,supports_thread_scope=excluded.supports_thread_scope,
          supports_user_apps=excluded.supports_user_apps,supports_mini_apps=excluded.supports_mini_apps,
          supports_embedding=excluded.supports_embedding,agent_interaction=excluded.agent_interaction,
          deterministic_without_model=excluded.deterministic_without_model,state_ownership=excluded.state_ownership,
          maturity=excluded.maturity,is_built_in=excluded.is_built_in,is_enabled=excluded.is_enabled,updated_at=excluded.updated_at;
        """;
}
