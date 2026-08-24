using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed class ApiBankService(ISqliteConnectionFactory factory) : IApiBank
{
    public async Task<ApiBankRecord> UpsertAsync(ApiBankRecord record, CancellationToken cancellationToken)
    {
        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        KnowledgeContentSafety.ThrowIfContainsSecret(
            record.Application,
            record.ApiName,
            record.Version,
            record.ActionsJson,
            record.Authentication,
            record.InputsJson,
            record.OutputsJson,
            record.ScopesJson,
            record.RateLimits,
            record.Pricing,
            record.CapabilityNotes,
            record.Limitations,
            record.OfflineQueuePolicy);

        var estimate = KnowledgeContentSafety.Utf8Bytes(
            record.Application, record.ApiName, record.Version, record.DocumentationUrl, record.ActionsJson,
            record.Authentication, record.AlternativesJson, record.Deprecation, record.DocumentationHash,
            record.InputsJson, record.OutputsJson, record.ScopesJson, record.RateLimits, record.Pricing,
            record.CapabilityNotes, record.Limitations, record.OfflineQueuePolicy, record.SourceUrl) + 256;
        var bytesWithoutCurrent = await GetBytesExcludingAsync(record.Id, cancellationToken).ConfigureAwait(false);
        if (bytesWithoutCurrent + estimate > KnowledgeStorageLimits.ApiBankBytes)
            throw new InvalidOperationException("API Bank storage limit reached. Clean up stored API knowledge before adding more.");

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO api_bank_records(id,application,api_name,version,documentation_url,actions_json,authentication,requires_internet,requires_credentials,cost_per_request,alternatives_json,deprecation,last_checked_at,documentation_hash)
                VALUES($id,$application,$api,$version,$url,$actions,$auth,$internet,$credentials,$cost,$alternatives,$deprecation,$checked,$hash)
                ON CONFLICT(id) DO UPDATE SET application=excluded.application,api_name=excluded.api_name,version=excluded.version,
                  documentation_url=excluded.documentation_url,actions_json=excluded.actions_json,authentication=excluded.authentication,
                  requires_internet=excluded.requires_internet,requires_credentials=excluded.requires_credentials,cost_per_request=excluded.cost_per_request,
                  alternatives_json=excluded.alternatives_json,deprecation=excluded.deprecation,last_checked_at=excluded.last_checked_at,
                  documentation_hash=excluded.documentation_hash;
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
        }

        await using (var detail = connection.CreateCommand())
        {
            detail.Transaction = transaction;
            detail.CommandText = """
                INSERT INTO api_bank_details(id,inputs_json,outputs_json,scopes_json,rate_limits,pricing,capability_notes,limitations,offline_queue_policy,is_pinned,source_url)
                VALUES($id,$inputs,$outputs,$scopes,$rate,$pricing,$notes,$limits,$offline,$pinned,$source)
                ON CONFLICT(id) DO UPDATE SET inputs_json=excluded.inputs_json,outputs_json=excluded.outputs_json,
                  scopes_json=excluded.scopes_json,rate_limits=excluded.rate_limits,pricing=excluded.pricing,
                  capability_notes=excluded.capability_notes,limitations=excluded.limitations,
                  offline_queue_policy=excluded.offline_queue_policy,is_pinned=excluded.is_pinned,source_url=excluded.source_url;
                """;
            detail.Parameters.AddWithValue("$id", record.Id.ToString());
            detail.Parameters.AddWithValue("$inputs", record.InputsJson);
            detail.Parameters.AddWithValue("$outputs", record.OutputsJson);
            detail.Parameters.AddWithValue("$scopes", record.ScopesJson);
            detail.Parameters.AddWithValue("$rate", record.RateLimits);
            detail.Parameters.AddWithValue("$pricing", record.Pricing);
            detail.Parameters.AddWithValue("$notes", record.CapabilityNotes);
            detail.Parameters.AddWithValue("$limits", record.Limitations);
            detail.Parameters.AddWithValue("$offline", record.OfflineQueuePolicy);
            detail.Parameters.AddWithValue("$pinned", record.IsPinned ? 1 : 0);
            detail.Parameters.AddWithValue("$source", record.SourceUrl);
            await detail.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<IReadOnlyList<ApiBankRecord>> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var value = query?.Trim() ?? string.Empty;
        command.CommandText = """
            SELECT r.id,r.application,r.api_name,r.version,r.documentation_url,r.actions_json,r.authentication,
                   r.requires_internet,r.requires_credentials,r.cost_per_request,r.alternatives_json,r.deprecation,
                   r.last_checked_at,r.documentation_hash,
                   COALESCE(d.inputs_json,'[]'),COALESCE(d.outputs_json,'[]'),COALESCE(d.scopes_json,'[]'),
                   COALESCE(d.rate_limits,''),COALESCE(d.pricing,''),COALESCE(d.capability_notes,''),
                   COALESCE(d.limitations,''),COALESCE(d.offline_queue_policy,''),COALESCE(d.is_pinned,0),
                   COALESCE(d.source_url,'')
            FROM api_bank_records r
            LEFT JOIN api_bank_details d ON d.id=r.id
            WHERE $query='' OR r.application LIKE $like OR r.api_name LIKE $like OR r.actions_json LIKE $like
            ORDER BY COALESCE(d.is_pinned,0) DESC,r.application,r.api_name
            LIMIT 300;
            """;
        command.Parameters.AddWithValue("$query", value);
        command.Parameters.AddWithValue("$like", $"%{value}%");
        var result = new List<ApiBankRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new ApiBankRecord(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7) != 0,
                reader.GetInt32(8) != 0,
                reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                DateTimeOffset.Parse(reader.GetString(12)),
                reader.GetString(13),
                reader.GetString(14),
                reader.GetString(15),
                reader.GetString(16),
                reader.GetString(17),
                reader.GetString(18),
                reader.GetString(19),
                reader.GetString(20),
                reader.GetString(21),
                reader.GetInt32(22) != 0,
                reader.GetString(23)));
        }
        return result;
    }

    public async Task<bool> SetPinnedAsync(Guid id, bool pinned, CancellationToken cancellationToken)
    {
        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO api_bank_details(id,is_pinned)
            VALUES($id,$pinned)
            ON CONFLICT(id) DO UPDATE SET is_pinned=excluded.is_pinned;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$pinned", pinned ? 1 : 0);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
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

    private async Task<long> GetBytesExcludingAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(length(r.application)+length(r.api_name)+length(r.version)+length(r.documentation_url)+
                length(r.actions_json)+length(r.authentication)+length(r.alternatives_json)+length(r.documentation_hash)+
                length(COALESCE(d.inputs_json,''))+length(COALESCE(d.outputs_json,''))+length(COALESCE(d.scopes_json,''))+
                length(COALESCE(d.rate_limits,''))+length(COALESCE(d.pricing,''))+length(COALESCE(d.capability_notes,''))+
                length(COALESCE(d.limitations,''))+length(COALESCE(d.offline_queue_policy,''))+length(COALESCE(d.source_url,''))),0)
            FROM api_bank_records r
            LEFT JOIN api_bank_details d ON d.id=r.id
            WHERE r.id<>$id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed class BackgroundLearningScheduler : IBackgroundLearningScheduler
{
    private readonly ISqliteConnectionFactory? _factory;
    private readonly IPrivacyPreferenceStore? _privacy;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<Guid, BackgroundLearningTask> _tasks = [];
    private readonly HashSet<KnowledgeCategory> _disabledCategories = [];
    private bool _initialized;
    private bool _globalEnabled = true;
    private BackgroundLearningMode _mode = BackgroundLearningMode.Balanced;
    private DateTimeOffset? _lastChangedAt;

    public BackgroundLearningScheduler(IPrivacyPreferenceStore privacy)
    {
        _privacy = privacy;
    }

    public BackgroundLearningScheduler(IPrivacyPreferenceStore privacy, ISqliteConnectionFactory factory)
    {
        _privacy = privacy;
        _factory = factory;
    }

    public BackgroundLearningScheduler(ISqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public BackgroundLearningMode Mode
    {
        get { lock (_gate) return _mode; }
    }

    public bool IsGloballyEnabled
    {
        get { lock (_gate) return _globalEnabled && (_privacy?.Current.BackgroundLearningEnabled ?? true); }
    }

    public bool IsEnabled(KnowledgeCategory category)
    {
        lock (_gate) return (_privacy?.Current.BackgroundLearningEnabled ?? true) && _globalEnabled && !_disabledCategories.Contains(category);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            if (_factory is null)
            {
                _initialized = true;
                return;
            }

            await KnowledgeSchema.EnsureAsync(_factory, cancellationToken).ConfigureAwait(false);
            await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var settings = connection.CreateCommand())
            {
                settings.CommandText = """
                    INSERT OR IGNORE INTO background_learning_settings(id,global_enabled,mode,disabled_categories_json,updated_at)
                    VALUES(1,1,1,'[]',$now);
                    SELECT global_enabled,mode,disabled_categories_json,updated_at
                    FROM background_learning_settings WHERE id=1;
                    """;
                settings.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                await using var reader = await settings.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    lock (_gate)
                    {
                        _globalEnabled = reader.GetInt32(0) != 0;
                        _mode = (BackgroundLearningMode)reader.GetInt32(1);
                        var values = JsonSerializer.Deserialize<int[]>(reader.GetString(2)) ?? [];
                        _disabledCategories.Clear();
                        foreach (var value in values)
                            if (Enum.IsDefined(typeof(KnowledgeCategory), value))
                                _disabledCategories.Add((KnowledgeCategory)value);
                        _lastChangedAt = DateTimeOffset.Parse(reader.GetString(3));
                    }
                }
            }

            await using (var tasks = connection.CreateCommand())
            {
                tasks.CommandText = """
                    SELECT id,title,category,priority,status,created_at,source,started_at,last_run_at,completed_at,result,error,requires_network,requires_model
                    FROM background_learning_tasks
                    ORDER BY created_at DESC
                    LIMIT 500;
                    """;
                await using var reader = await tasks.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var task = ReadTask(reader);
                    lock (_gate) _tasks[task.Id] = task;
                }
            }
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public async Task SetGlobalEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _globalEnabled = enabled;
            _lastChangedAt = DateTimeOffset.UtcNow;
        }
        await PersistSettingsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetModeAsync(BackgroundLearningMode mode, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _mode = mode;
            _lastChangedAt = DateTimeOffset.UtcNow;
        }
        await PersistSettingsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetCategoryEnabledAsync(KnowledgeCategory category, bool enabled, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (enabled) _disabledCategories.Remove(category);
            else _disabledCategories.Add(category);
            _lastChangedAt = DateTimeOffset.UtcNow;
        }
        await PersistSettingsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BackgroundLearningTask> EnqueueAsync(
        string title,
        KnowledgeCategory category,
        BackgroundLearningPriority priority,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!IsGloballyEnabled) throw new InvalidOperationException("Background Learning is disabled.");
        if (!IsEnabled(category)) throw new InvalidOperationException($"Background Learning is disabled for {category}.");
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("A task title is required.", nameof(title));

        var task = new BackgroundLearningTask(
            Guid.NewGuid(), title.Trim(), category, priority, BackgroundLearningTaskStatus.Queued, DateTimeOffset.UtcNow);
        lock (_gate) _tasks[task.Id] = task;
        await PersistTaskAsync(task, cancellationToken).ConfigureAwait(false);
        return task;
    }

    public async Task<IReadOnlyList<BackgroundLearningTask>> ListAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate) return _tasks.Values.OrderByDescending(task => task.CreatedAt).ToArray();
    }

    public async Task<bool> PauseAsync(Guid id, CancellationToken cancellationToken)
        => await ChangeStatusAsync(id, BackgroundLearningTaskStatus.Paused, cancellationToken).ConfigureAwait(false);

    public async Task<bool> ResumeAsync(Guid id, CancellationToken cancellationToken)
        => await ChangeStatusAsync(id, BackgroundLearningTaskStatus.Queued, cancellationToken).ConfigureAwait(false);

    public async Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken)
        => await ChangeStatusAsync(id, BackgroundLearningTaskStatus.Cancelled, cancellationToken).ConfigureAwait(false);

    public async Task<BackgroundLearningSchedulerSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            var categories = Enum.GetValues<KnowledgeCategory>()
                .ToDictionary(category => category, category => _globalEnabled && !_disabledCategories.Contains(category));
            return new BackgroundLearningSchedulerSnapshot(
                _globalEnabled,
                _mode,
                categories,
                _tasks.Values.OrderByDescending(task => task.CreatedAt).ToArray(),
                _lastChangedAt);
        }
    }

    public bool CanRun(BackgroundLearningTask task, BackgroundLearningResourceState resources)
    {
        if (!IsEnabled(task.Category)) return false;
        if (task.Status != BackgroundLearningTaskStatus.Queued) return false;
        if (resources.IsForegroundBusy || resources.IsModelBusy && task.RequiresModel) return false;
        if (task.RequiresNetwork && !resources.HasNetwork) return false;
        if (Mode is BackgroundLearningMode.Minimal or BackgroundLearningMode.Balanced && resources.IsOnBattery) return false;
        if (Mode != BackgroundLearningMode.Maximum && task.RequiresNetwork && resources.IsMetered) return false;
        return true;
    }

    public IReadOnlyList<BackgroundLearningTask> Snapshot()
    {
        lock (_gate) return _tasks.Values.OrderByDescending(task => task.CreatedAt).ToArray();
    }

    public void SetMode(BackgroundLearningMode mode)
    {
        lock (_gate) _mode = mode;
    }

    public void SetCategoryEnabled(KnowledgeCategory category, bool enabled)
    {
        lock (_gate)
        {
            if (enabled) _disabledCategories.Remove(category);
            else _disabledCategories.Add(category);
        }
    }

    public async Task<bool> UpdateExecutionStateAsync(
        Guid id,
        BackgroundLearningTaskStatus status,
        string? result,
        string? error,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        BackgroundLearningTask updated;
        lock (_gate)
        {
            if (!_tasks.TryGetValue(id, out var current)) return false;
            var now = DateTimeOffset.UtcNow;
            updated = current with
            {
                Status = status,
                StartedAt = current.StartedAt ?? (status == BackgroundLearningTaskStatus.Running ? now : null),
                LastRunAt = status is BackgroundLearningTaskStatus.Running or BackgroundLearningTaskStatus.Completed or BackgroundLearningTaskStatus.Failed
                    ? now
                    : current.LastRunAt,
                CompletedAt = status is BackgroundLearningTaskStatus.Completed or BackgroundLearningTaskStatus.Failed or BackgroundLearningTaskStatus.Cancelled
                    ? now
                    : null,
                Result = result,
                Error = error
            };
            _tasks[id] = updated;
        }
        await PersistTaskAsync(updated, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ChangeStatusAsync(Guid id, BackgroundLearningTaskStatus status, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        BackgroundLearningTask updated;
        lock (_gate)
        {
            if (!_tasks.TryGetValue(id, out var current)) return false;
            if (current.Status is BackgroundLearningTaskStatus.Completed or BackgroundLearningTaskStatus.Failed or BackgroundLearningTaskStatus.Cancelled)
                return false;
            updated = current with
            {
                Status = status,
                CompletedAt = status == BackgroundLearningTaskStatus.Cancelled ? DateTimeOffset.UtcNow : current.CompletedAt
            };
            _tasks[id] = updated;
        }
        await PersistTaskAsync(updated, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task PersistSettingsAsync(CancellationToken cancellationToken)
    {
        if (_factory is null) return;
        bool enabled;
        BackgroundLearningMode mode;
        int[] disabled;
        DateTimeOffset changed;
        lock (_gate)
        {
            enabled = _globalEnabled;
            mode = _mode;
            disabled = _disabledCategories.Select(category => (int)category).OrderBy(value => value).ToArray();
            changed = _lastChangedAt ?? DateTimeOffset.UtcNow;
        }

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO background_learning_settings(id,global_enabled,mode,disabled_categories_json,updated_at)
            VALUES(1,$enabled,$mode,$categories,$updated)
            ON CONFLICT(id) DO UPDATE SET global_enabled=excluded.global_enabled,mode=excluded.mode,
              disabled_categories_json=excluded.disabled_categories_json,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$mode", (int)mode);
        command.Parameters.AddWithValue("$categories", JsonSerializer.Serialize(disabled));
        command.Parameters.AddWithValue("$updated", changed.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistTaskAsync(BackgroundLearningTask task, CancellationToken cancellationToken)
    {
        if (_factory is null) return;
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO background_learning_tasks(id,title,category,priority,status,created_at,source,started_at,last_run_at,completed_at,result,error,requires_network,requires_model)
            VALUES($id,$title,$category,$priority,$status,$created,$source,$started,$last,$completed,$result,$error,$network,$model)
            ON CONFLICT(id) DO UPDATE SET title=excluded.title,category=excluded.category,priority=excluded.priority,status=excluded.status,
              source=excluded.source,started_at=excluded.started_at,last_run_at=excluded.last_run_at,completed_at=excluded.completed_at,
              result=excluded.result,error=excluded.error,requires_network=excluded.requires_network,requires_model=excluded.requires_model;
            """;
        command.Parameters.AddWithValue("$id", task.Id.ToString());
        command.Parameters.AddWithValue("$title", task.Title);
        command.Parameters.AddWithValue("$category", (int)task.Category);
        command.Parameters.AddWithValue("$priority", (int)task.Priority);
        command.Parameters.AddWithValue("$status", (int)task.Status);
        command.Parameters.AddWithValue("$created", task.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$source", task.Source);
        command.Parameters.AddWithValue("$started", task.StartedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$last", task.LastRunAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completed", task.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$result", task.Result ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$error", task.Error ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$network", task.RequiresNetwork ? 1 : 0);
        command.Parameters.AddWithValue("$model", task.RequiresModel ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static BackgroundLearningTask ReadTask(SqliteDataReader reader)
        => new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            (KnowledgeCategory)reader.GetInt32(2),
            (BackgroundLearningPriority)reader.GetInt32(3),
            (BackgroundLearningTaskStatus)reader.GetInt32(4),
            DateTimeOffset.Parse(reader.GetString(5)),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
            reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
            reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetInt32(12) != 0,
            reader.GetInt32(13) != 0);
}
