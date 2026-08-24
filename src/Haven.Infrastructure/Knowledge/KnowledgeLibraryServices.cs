using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

internal static class KnowledgeSchema
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task EnsureAsync(ISqliteConnectionFactory factory, CancellationToken cancellationToken)
    {
        await RetrievalSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS knowledge_records(
                    id TEXT PRIMARY KEY,
                    category INTEGER NOT NULL,
                    topic TEXT NOT NULL,
                    title TEXT NOT NULL,
                    summary TEXT NOT NULL,
                    privacy_class INTEGER NOT NULL,
                    confidence REAL NOT NULL,
                    is_pinned INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    expires_at TEXT NULL,
                    learned_because TEXT NOT NULL,
                    sources_json TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_knowledge_records_category ON knowledge_records(category,updated_at);

                CREATE TABLE IF NOT EXISTS knowledge_record_details(
                    id TEXT PRIMARY KEY REFERENCES knowledge_records(id) ON DELETE CASCADE,
                    freshness INTEGER NOT NULL DEFAULT 0,
                    last_confirmed_at TEXT NULL,
                    scope TEXT NOT NULL DEFAULT 'global',
                    status INTEGER NOT NULL DEFAULT 0,
                    origin INTEGER NOT NULL DEFAULT 0,
                    user_correction TEXT NULL,
                    supersedes_id TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_knowledge_details_status ON knowledge_record_details(status,last_confirmed_at);

                CREATE TABLE IF NOT EXISTS knowledge_rejections(
                    fingerprint TEXT PRIMARY KEY,
                    record_id TEXT NULL,
                    reason TEXT NULL,
                    rejected_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS api_bank_records(
                    id TEXT PRIMARY KEY,
                    application TEXT NOT NULL,
                    api_name TEXT NOT NULL,
                    version TEXT NOT NULL,
                    documentation_url TEXT NOT NULL,
                    actions_json TEXT NOT NULL,
                    authentication TEXT NOT NULL,
                    requires_internet INTEGER NOT NULL,
                    requires_credentials INTEGER NOT NULL,
                    cost_per_request TEXT NULL,
                    alternatives_json TEXT NOT NULL,
                    deprecation TEXT NULL,
                    last_checked_at TEXT NOT NULL,
                    documentation_hash TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_api_bank_name ON api_bank_records(application,api_name);

                CREATE TABLE IF NOT EXISTS api_bank_details(
                    id TEXT PRIMARY KEY REFERENCES api_bank_records(id) ON DELETE CASCADE,
                    inputs_json TEXT NOT NULL DEFAULT '[]',
                    outputs_json TEXT NOT NULL DEFAULT '[]',
                    scopes_json TEXT NOT NULL DEFAULT '[]',
                    rate_limits TEXT NOT NULL DEFAULT '',
                    pricing TEXT NOT NULL DEFAULT '',
                    capability_notes TEXT NOT NULL DEFAULT '',
                    limitations TEXT NOT NULL DEFAULT '',
                    offline_queue_policy TEXT NOT NULL DEFAULT '',
                    is_pinned INTEGER NOT NULL DEFAULT 0,
                    source_url TEXT NOT NULL DEFAULT ''
                );

                CREATE TABLE IF NOT EXISTS background_learning_settings(
                    id INTEGER PRIMARY KEY CHECK(id=1),
                    global_enabled INTEGER NOT NULL DEFAULT 1,
                    mode INTEGER NOT NULL DEFAULT 1,
                    disabled_categories_json TEXT NOT NULL DEFAULT '[]',
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS background_learning_tasks(
                    id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    category INTEGER NOT NULL,
                    priority INTEGER NOT NULL,
                    status INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    source TEXT NOT NULL,
                    started_at TEXT NULL,
                    last_run_at TEXT NULL,
                    completed_at TEXT NULL,
                    result TEXT NULL,
                    error TEXT NULL,
                    requires_network INTEGER NOT NULL DEFAULT 0,
                    requires_model INTEGER NOT NULL DEFAULT 1
                );
                CREATE INDEX IF NOT EXISTS ix_background_learning_tasks_status
                    ON background_learning_tasks(status,created_at DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await using var migration = connection.CreateCommand();
            migration.CommandText = "INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(11,$now);";
            migration.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await migration.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}

internal static class KnowledgeContentSafety
{
    public static void ThrowIfContainsSecret(params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var candidate = value.Trim();
            if (candidate.Contains("-----BEGIN " + "PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("-----BEGIN RSA " + "PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("client_secret=", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("access_token=", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("refresh_token=", StringComparison.OrdinalIgnoreCase) ||
                ContainsLongTokenAfter(candidate, "Bearer ") ||
                ContainsLongTokenAfter(candidate, "sk-") ||
                ContainsAwsAccessKey(candidate))
            {
                throw new InvalidOperationException("Credentials and secret values cannot be stored in Haven Library or API Bank.");
            }
        }
    }

    public static string Fingerprint(string value)
    {
        var normalized = string.Join(' ', value.Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static int Utf8Bytes(params string?[] values)
        => values.Where(static value => value is not null).Sum(value => Encoding.UTF8.GetByteCount(value!));

    private static bool ContainsLongTokenAfter(string value, string prefix)
    {
        var start = value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return false;
        start += prefix.Length;
        var length = 0;
        while (start + length < value.Length)
        {
            var c = value[start + length];
            if (!(char.IsLetterOrDigit(c) || c is '_' or '-' or '.')) break;
            length++;
        }
        return length >= 20;
    }

    private static bool ContainsAwsAccessKey(string value)
    {
        for (var index = 0; index <= value.Length - 20; index++)
        {
            if (!value.AsSpan(index, 4).Equals("AKIA", StringComparison.Ordinal)) continue;
            if (value.AsSpan(index + 4, 16).ToString().All(static c => char.IsLetterOrDigit(c))) return true;
        }
        return false;
    }
}

public sealed class KnowledgeLibraryService(
    ISqliteConnectionFactory factory,
    IRetrievalIndexService retrieval) : IKnowledgeLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<KnowledgeRecord> UpsertAsync(KnowledgeRecord record, string indexedText, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.PrivacyClass == KnowledgePrivacyClass.NeverLearn)
            throw new InvalidOperationException("Never Learn records cannot be stored.");
        if (record.Confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(record));

        KnowledgeContentSafety.ThrowIfContainsSecret(
            record.Topic, record.Title, record.Summary, record.LearnedBecause, indexedText, record.UserCorrection);

        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        if (record.Origin == KnowledgeOrigin.Inferred &&
            await IsRejectedAsync(KnowledgeContentSafety.Fingerprint(record.Summary), cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Previously rejected knowledge cannot be re-learned without an explicit user correction.");
        }

        var sourcesJson = JsonSerializer.Serialize(record.Sources, JsonOptions);
        var estimate = KnowledgeContentSafety.Utf8Bytes(
            record.Topic, record.Title, record.Summary, record.LearnedBecause, sourcesJson, indexedText,
            record.Scope, record.UserCorrection) + 256;
        var bytesWithoutCurrent = await GetKnowledgeBytesExcludingAsync(record.Id, cancellationToken).ConfigureAwait(false);
        if (bytesWithoutCurrent + estimate > KnowledgeStorageLimits.BackgroundLearningBytes)
            throw new InvalidOperationException("Background Learning storage limit reached. Clean up stored knowledge before adding more.");

        var scope = new RetrievalScope(RetrievalScopeKind.Collection, record.Id);
        var isExpired = record.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow &&
                        record.Freshness != KnowledgeFreshnessClass.Durable;
        var isRetrievable = record.Status is KnowledgeRecordStatus.Active or KnowledgeRecordStatus.Corrected && !isExpired;
        if (isRetrievable)
        {
            var content = string.IsNullOrWhiteSpace(record.UserCorrection) ? indexedText : record.UserCorrection!;
            await retrieval.IndexTextAsync(scope, "knowledge", record.Id.ToString(), record.Title, content, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await retrieval.RemoveSourceAsync(scope, "knowledge", record.Id.ToString(), cancellationToken).ConfigureAwait(false);
        }

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO knowledge_records(id,category,topic,title,summary,privacy_class,confidence,is_pinned,created_at,updated_at,expires_at,learned_because,sources_json)
                VALUES($id,$category,$topic,$title,$summary,$privacy,$confidence,$pinned,$created,$updated,$expires,$because,$sources)
                ON CONFLICT(id) DO UPDATE SET category=excluded.category,topic=excluded.topic,title=excluded.title,summary=excluded.summary,
                  privacy_class=excluded.privacy_class,confidence=excluded.confidence,is_pinned=excluded.is_pinned,updated_at=excluded.updated_at,
                  expires_at=excluded.expires_at,learned_because=excluded.learned_because,sources_json=excluded.sources_json;
                """;
            command.Parameters.AddWithValue("$id", record.Id.ToString());
            command.Parameters.AddWithValue("$category", (int)record.Category);
            command.Parameters.AddWithValue("$topic", record.Topic);
            command.Parameters.AddWithValue("$title", record.Title);
            command.Parameters.AddWithValue("$summary", record.Summary);
            command.Parameters.AddWithValue("$privacy", (int)record.PrivacyClass);
            command.Parameters.AddWithValue("$confidence", record.Confidence);
            command.Parameters.AddWithValue("$pinned", record.IsPinned ? 1 : 0);
            command.Parameters.AddWithValue("$created", record.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$updated", record.UpdatedAt.ToString("O"));
            command.Parameters.AddWithValue("$expires", record.ExpiresAt?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$because", record.LearnedBecause);
            command.Parameters.AddWithValue("$sources", sourcesJson);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var details = connection.CreateCommand())
        {
            details.Transaction = transaction;
            details.CommandText = """
                INSERT INTO knowledge_record_details(id,freshness,last_confirmed_at,scope,status,origin,user_correction,supersedes_id)
                VALUES($id,$freshness,$confirmed,$scope,$status,$origin,$correction,$supersedes)
                ON CONFLICT(id) DO UPDATE SET freshness=excluded.freshness,last_confirmed_at=excluded.last_confirmed_at,
                  scope=excluded.scope,status=excluded.status,origin=excluded.origin,user_correction=excluded.user_correction,
                  supersedes_id=excluded.supersedes_id;
                """;
            details.Parameters.AddWithValue("$id", record.Id.ToString());
            details.Parameters.AddWithValue("$freshness", (int)record.Freshness);
            details.Parameters.AddWithValue("$confirmed", record.LastConfirmedAt?.ToString("O") ?? (object)DBNull.Value);
            details.Parameters.AddWithValue("$scope", record.Scope);
            details.Parameters.AddWithValue("$status", (int)record.Status);
            details.Parameters.AddWithValue("$origin", (int)record.Origin);
            details.Parameters.AddWithValue("$correction", record.UserCorrection ?? (object)DBNull.Value);
            details.Parameters.AddWithValue("$supersedes", record.SupersedesId?.ToString() ?? (object)DBNull.Value);
            await details.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<IReadOnlyList<KnowledgeRecord>> SearchMetadataAsync(
        string? query,
        KnowledgeCategory? category,
        CancellationToken cancellationToken)
    {
        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.id,r.category,r.topic,r.title,r.summary,r.privacy_class,r.confidence,r.is_pinned,
                   r.created_at,r.updated_at,r.expires_at,r.learned_because,r.sources_json,
                   COALESCE(d.freshness,0),d.last_confirmed_at,COALESCE(d.scope,'global'),
                   COALESCE(d.status,0),COALESCE(d.origin,0),d.user_correction,d.supersedes_id
            FROM knowledge_records r
            LEFT JOIN knowledge_record_details d ON d.id=r.id
            WHERE ($query='' OR r.title LIKE $like OR r.topic LIKE $like OR r.summary LIKE $like)
              AND ($category=-1 OR r.category=$category)
            ORDER BY r.is_pinned DESC,COALESCE(d.status,0),r.updated_at DESC
            LIMIT 300;
            """;
        var value = query?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("$query", value);
        command.Parameters.AddWithValue("$like", $"%{value}%");
        command.Parameters.AddWithValue("$category", category.HasValue ? (int)category.Value : -1);
        var result = new List<KnowledgeRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(ReadRecord(reader));
        return result;
    }

    public async Task<KnowledgeRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var records = await SearchMetadataAsync(null, null, cancellationToken).ConfigureAwait(false);
        return records.FirstOrDefault(record => record.Id == id);
    }

    public async Task<bool> SetPinnedAsync(Guid id, bool pinned, CancellationToken cancellationToken)
    {
        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE knowledge_records SET is_pinned=$pinned,updated_at=$updated WHERE id=$id;";
        command.Parameters.AddWithValue("$pinned", pinned ? 1 : 0);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id.ToString());
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<KnowledgeRecord> CorrectAsync(
        Guid id,
        string correctedSummary,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correctedSummary)) throw new ArgumentException("A correction is required.", nameof(correctedSummary));
        var current = await GetAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Knowledge record was not found.");
        KnowledgeContentSafety.ThrowIfContainsSecret(correctedSummary, reason);

        await SetStatusAsync(current.Id, KnowledgeRecordStatus.Superseded, reason, cancellationToken).ConfigureAwait(false);
        await retrieval.RemoveSourceAsync(
            new RetrievalScope(RetrievalScopeKind.Collection, current.Id),
            "knowledge",
            current.Id.ToString(),
            cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var corrected = current with
        {
            Id = Guid.NewGuid(),
            Summary = correctedSummary.Trim(),
            Confidence = 1,
            IsPinned = current.IsPinned,
            CreatedAt = now,
            UpdatedAt = now,
            LastConfirmedAt = now,
            Status = KnowledgeRecordStatus.Corrected,
            Origin = KnowledgeOrigin.Explicit,
            UserCorrection = string.IsNullOrWhiteSpace(reason) ? "User correction" : reason.Trim(),
            SupersedesId = current.Id,
            ExpiresAt = current.Freshness == KnowledgeFreshnessClass.Durable ? current.ExpiresAt : null
        };
        return await UpsertAsync(corrected, corrected.Summary, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RejectAsync(Guid id, string? reason, CancellationToken cancellationToken)
    {
        var current = await GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (current is null) return false;
        await SetStatusAsync(id, KnowledgeRecordStatus.Rejected, reason, cancellationToken).ConfigureAwait(false);
        await retrieval.RemoveSourceAsync(
            new RetrievalScope(RetrievalScopeKind.Collection, id),
            "knowledge",
            id.ToString(),
            cancellationToken).ConfigureAwait(false);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO knowledge_rejections(fingerprint,record_id,reason,rejected_at)
            VALUES($fingerprint,$record,$reason,$at)
            ON CONFLICT(fingerprint) DO UPDATE SET record_id=excluded.record_id,reason=excluded.reason,rejected_at=excluded.rejected_at;
            """;
        command.Parameters.AddWithValue("$fingerprint", KnowledgeContentSafety.Fingerprint(current.Summary));
        command.Parameters.AddWithValue("$record", current.Id.ToString());
        command.Parameters.AddWithValue("$reason", reason ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> ForgetAsync(Guid id, CancellationToken cancellationToken, bool preserveRejection = false)
    {
        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!preserveRejection)
        {
            await using var rejection = connection.CreateCommand();
            rejection.Transaction = transaction;
            rejection.CommandText = "DELETE FROM knowledge_rejections WHERE record_id=$id;";
            rejection.Parameters.AddWithValue("$id", id.ToString());
            await rejection.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        var changed = false;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM knowledge_records WHERE id=$id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            changed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await retrieval.RemoveSourceAsync(
            new RetrievalScope(RetrievalScopeKind.Collection, id),
            "knowledge",
            id.ToString(),
            cancellationToken).ConfigureAwait(false);
        return changed;
    }

    public async Task<int> ForgetCategoryAsync(KnowledgeCategory category, CancellationToken cancellationToken)
    {
        var records = await SearchMetadataAsync(null, category, cancellationToken).ConfigureAwait(false);
        var candidates = records.Where(record => !record.IsPinned).ToArray();
        foreach (var record in candidates)
            await ForgetAsync(record.Id, cancellationToken).ConfigureAwait(false);
        return candidates.Length;
    }

    private async Task SetStatusAsync(
        Guid id,
        KnowledgeRecordStatus status,
        string? correction,
        CancellationToken cancellationToken)
    {
        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var detail = connection.CreateCommand())
        {
            detail.Transaction = transaction;
            detail.CommandText = """
                INSERT INTO knowledge_record_details(id,status,user_correction)
                VALUES($id,$status,$correction)
                ON CONFLICT(id) DO UPDATE SET status=excluded.status,user_correction=excluded.user_correction;
                """;
            detail.Parameters.AddWithValue("$id", id.ToString());
            detail.Parameters.AddWithValue("$status", (int)status);
            detail.Parameters.AddWithValue("$correction", correction ?? (object)DBNull.Value);
            await detail.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var record = connection.CreateCommand())
        {
            record.Transaction = transaction;
            record.CommandText = "UPDATE knowledge_records SET updated_at=$updated WHERE id=$id;";
            record.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            record.Parameters.AddWithValue("$id", id.ToString());
            await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsRejectedAsync(string fingerprint, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM knowledge_rejections WHERE fingerprint=$fingerprint;";
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private async Task<long> GetKnowledgeBytesExcludingAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(
                length(r.topic)+length(r.title)+length(r.summary)+length(r.learned_because)+length(r.sources_json)+
                length(COALESCE(d.scope,''))+length(COALESCE(d.user_correction,''))),0)
            FROM knowledge_records r
            LEFT JOIN knowledge_record_details d ON d.id=r.id
            WHERE r.id<>$id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static KnowledgeRecord ReadRecord(SqliteDataReader reader)
        => new(
            Guid.Parse(reader.GetString(0)),
            (KnowledgeCategory)reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            (KnowledgePrivacyClass)reader.GetInt32(5),
            reader.GetDouble(6),
            reader.GetInt32(7) != 0,
            DateTimeOffset.Parse(reader.GetString(8)),
            DateTimeOffset.Parse(reader.GetString(9)),
            reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
            reader.GetString(11),
            JsonSerializer.Deserialize<IReadOnlyList<KnowledgeSource>>(reader.GetString(12), JsonOptions) ?? [],
            (KnowledgeFreshnessClass)reader.GetInt32(13),
            reader.IsDBNull(14) ? null : DateTimeOffset.Parse(reader.GetString(14)),
            reader.GetString(15),
            (KnowledgeRecordStatus)reader.GetInt32(16),
            (KnowledgeOrigin)reader.GetInt32(17),
            reader.IsDBNull(18) ? null : reader.GetString(18),
            reader.IsDBNull(19) ? null : Guid.Parse(reader.GetString(19)));
}

public sealed class KnowledgeMaintenanceService(
    ISqliteConnectionFactory factory,
    IKnowledgeLibrary knowledge,
    IApiBank apiBank) : IKnowledgeMaintenanceService
{
    public async Task<KnowledgeStorageSnapshot> GetStorageAsync(CancellationToken cancellationToken)
    {
        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var knowledgeBytes = await ScalarAsync(connection, """
            SELECT COALESCE(SUM(length(r.topic)+length(r.title)+length(r.summary)+length(r.learned_because)+length(r.sources_json)+
                length(COALESCE(d.scope,''))+length(COALESCE(d.user_correction,''))),0)
            FROM knowledge_records r LEFT JOIN knowledge_record_details d ON d.id=r.id;
            """, cancellationToken).ConfigureAwait(false);
        var knowledgeCount = await ScalarAsync(connection, "SELECT COUNT(*) FROM knowledge_records;", cancellationToken).ConfigureAwait(false);
        var knowledgePinned = await ScalarAsync(connection, "SELECT COUNT(*) FROM knowledge_records WHERE is_pinned=1;", cancellationToken).ConfigureAwait(false);
        var apiBytes = await ScalarAsync(connection, """
            SELECT COALESCE(SUM(length(r.application)+length(r.api_name)+length(r.version)+length(r.documentation_url)+
                length(r.actions_json)+length(r.authentication)+length(r.alternatives_json)+length(r.documentation_hash)+
                length(COALESCE(d.inputs_json,''))+length(COALESCE(d.outputs_json,''))+length(COALESCE(d.scopes_json,''))+
                length(COALESCE(d.rate_limits,''))+length(COALESCE(d.pricing,''))+length(COALESCE(d.capability_notes,''))+
                length(COALESCE(d.limitations,''))+length(COALESCE(d.offline_queue_policy,''))+length(COALESCE(d.source_url,''))),0)
            FROM api_bank_records r LEFT JOIN api_bank_details d ON d.id=r.id;
            """, cancellationToken).ConfigureAwait(false);
        var apiCount = await ScalarAsync(connection, "SELECT COUNT(*) FROM api_bank_records;", cancellationToken).ConfigureAwait(false);
        var apiPinned = await ScalarAsync(connection, "SELECT COUNT(*) FROM api_bank_details WHERE is_pinned=1;", cancellationToken).ConfigureAwait(false);
        return new KnowledgeStorageSnapshot(
            knowledgeBytes, KnowledgeStorageLimits.BackgroundLearningBytes, checked((int)knowledgeCount), checked((int)knowledgePinned),
            apiBytes, KnowledgeStorageLimits.ApiBankBytes, checked((int)apiCount), checked((int)apiPinned));
    }

    public async Task<KnowledgeCleanupResult> CleanupAsync(CancellationToken cancellationToken)
    {
        var before = await GetStorageAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var knowledgeRecords = await knowledge.SearchMetadataAsync(null, null, cancellationToken).ConfigureAwait(false);
        var removable = knowledgeRecords
            .Where(record => !record.IsPinned)
            .OrderBy(record => CleanupRank(record, now))
            .ThenBy(record => record.UpdatedAt)
            .ToList();

        var removedKnowledge = 0;
        foreach (var record in removable)
        {
            var expiredOrDiscarded =
                record.Status is KnowledgeRecordStatus.Rejected or KnowledgeRecordStatus.Superseded ||
                record.ExpiresAt is { } expiry && expiry <= now && record.Freshness != KnowledgeFreshnessClass.Durable;
            var storage = await GetStorageAsync(cancellationToken).ConfigureAwait(false);
            if (!expiredOrDiscarded && storage.KnowledgeBytes <= storage.KnowledgeLimitBytes) break;
            if (await knowledge.ForgetAsync(
                    record.Id,
                    cancellationToken,
                    preserveRejection: record.Status == KnowledgeRecordStatus.Rejected).ConfigureAwait(false))
                removedKnowledge++;
        }

        var apiRecords = await apiBank.SearchAsync(null, cancellationToken).ConfigureAwait(false);
        var removedApi = 0;
        foreach (var record in apiRecords.Where(record => !record.IsPinned)
                     .OrderBy(record => string.IsNullOrWhiteSpace(record.Deprecation) ? 1 : 0)
                     .ThenBy(record => record.LastCheckedAt))
        {
            var storage = await GetStorageAsync(cancellationToken).ConfigureAwait(false);
            if (storage.ApiBankBytes <= storage.ApiBankLimitBytes) break;
            if (await apiBank.RemoveAsync(record.Id, cancellationToken).ConfigureAwait(false)) removedApi++;
        }

        var after = await GetStorageAsync(cancellationToken).ConfigureAwait(false);
        return new KnowledgeCleanupResult(
            removedKnowledge,
            removedApi,
            Math.Max(0, before.KnowledgeBytes - after.KnowledgeBytes),
            Math.Max(0, before.ApiBankBytes - after.ApiBankBytes),
            $"Removed {removedKnowledge} knowledge item(s) and {removedApi} API Bank item(s); pinned items were protected.");
    }

    private static int CleanupRank(KnowledgeRecord record, DateTimeOffset now)
    {
        if (record.Status is KnowledgeRecordStatus.Rejected or KnowledgeRecordStatus.Superseded) return 0;
        if (record.ExpiresAt is { } expiry && expiry <= now && record.Freshness != KnowledgeFreshnessClass.Durable) return 1;
        if (record.Confidence < .35) return 2;
        return 3;
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }
}
