using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class ApiBankService(ISqliteConnectionFactory factory) : IApiBank
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApiBankRecord> UpsertAsync(ApiBankRecord record, CancellationToken cancellationToken)
    {
        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO api_bank_records(id,application,api_name,version,documentation_url,actions_json,authentication,requires_internet,requires_credentials,cost_per_request,alternatives_json,deprecation,last_checked_at,documentation_hash)
            VALUES($id,$application,$api,$version,$url,$actions,$auth,$internet,$credentials,$cost,$alternatives,$deprecation,$checked,$hash)
            ON CONFLICT(id) DO UPDATE SET application=excluded.application,api_name=excluded.api_name,version=excluded.version,
              documentation_url=excluded.documentation_url,actions_json=excluded.actions_json,authentication=excluded.authentication,
              requires_internet=excluded.requires_internet,requires_credentials=excluded.requires_credentials,cost_per_request=excluded.cost_per_request,
              alternatives_json=excluded.alternatives_json,deprecation=excluded.deprecation,last_checked_at=excluded.last_checked_at,documentation_hash=excluded.documentation_hash;
            """;
        command.Parameters.AddWithValue("$id", record.Id.ToString());
        command.Parameters.AddWithValue("$application", record.Application);
        command.Parameters.AddWithValue("$api", record.ApiName);
        command.Parameters.AddWithValue("$version", record.Version);
        command.Parameters.AddWithValue("$url", record.DocumentationUrl);
        command.Parameters.AddWithValue("$actions", record.ActionsJson);
        command.Parameters.AddWithValue("$auth", record.Authentication);
        command.Parameters.AddWithValue("$internet", record.RequiresInternet ? 1 : 0);
        command.Parameters.AddWithValue("$credentials", record.RequiresCredentials ? 1 : 0);
        command.Parameters.AddWithValue("$cost", record.CostPerRequest.HasValue ? record.CostPerRequest.Value : (object)DBNull.Value);
        command.Parameters.AddWithValue("$alternatives", record.AlternativesJson);
        command.Parameters.AddWithValue("$deprecation", record.Deprecation ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$checked", record.LastCheckedAt.ToString("O"));
        command.Parameters.AddWithValue("$hash", record.DocumentationHash);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<IReadOnlyList<ApiBankRecord>> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var value = query?.Trim() ?? string.Empty;
        command.CommandText = "SELECT * FROM api_bank_records WHERE $query='' OR application LIKE $like OR api_name LIKE $like ORDER BY application,api_name LIMIT 200;";
        command.Parameters.AddWithValue("$query", value);
        command.Parameters.AddWithValue("$like", $"%{value}%");
        var result = new List<ApiBankRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new ApiBankRecord(
                Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetInt32(7) != 0, reader.GetInt32(8) != 0,
                reader.IsDBNull(9) ? null : reader.GetDecimal(9), reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetString(11),
                DateTimeOffset.Parse(reader.GetString(12)), reader.GetString(13)));
        }
        return result;
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM api_bank_records WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }
}

public sealed class BackgroundLearningScheduler(IPrivacyPreferenceStore privacy) : IBackgroundLearningScheduler
{
    private readonly object _gate = new();
    private readonly Queue<BackgroundLearningTask> _tasks = new();
    private readonly HashSet<KnowledgeCategory> _disabledCategories = [];

    public BackgroundLearningMode Mode { get; private set; } = BackgroundLearningMode.Balanced;
    public bool IsEnabled(KnowledgeCategory category)
    {
        lock (_gate) return privacy.Current.BackgroundLearningEnabled && !_disabledCategories.Contains(category);
    }

    public Task<BackgroundLearningTask> EnqueueAsync(string title, KnowledgeCategory category, BackgroundLearningPriority priority, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsEnabled(category)) throw new InvalidOperationException($"Background Learning is disabled for {category}.");
        var task = new BackgroundLearningTask(Guid.NewGuid(), title, category, priority, BackgroundLearningTaskStatus.Queued, DateTimeOffset.UtcNow);
        lock (_gate) _tasks.Enqueue(task);
        return Task.FromResult(task);
    }

    public IReadOnlyList<BackgroundLearningTask> Snapshot()
    {
        lock (_gate) return _tasks.ToArray();
    }

    public void SetMode(BackgroundLearningMode mode) => Mode = mode;
    public void SetCategoryEnabled(KnowledgeCategory category, bool enabled)
    {
        lock (_gate)
        {
            if (enabled) _disabledCategories.Remove(category);
            else _disabledCategories.Add(category);
        }
    }
}
