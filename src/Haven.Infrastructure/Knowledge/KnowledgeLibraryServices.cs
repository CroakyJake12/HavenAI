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
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using var migration = connection.CreateCommand();
            migration.CommandText = "INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(11,$now);";
            migration.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await migration.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
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
        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        var scope = new RetrievalScope(RetrievalScopeKind.Collection, record.Id);
        await retrieval.IndexTextAsync(scope, "knowledge", record.Id.ToString(), record.Title, indexedText, cancellationToken).ConfigureAwait(false);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
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
        command.Parameters.AddWithValue("$sources", JsonSerializer.Serialize(record.Sources, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<IReadOnlyList<KnowledgeRecord>> SearchMetadataAsync(string? query, KnowledgeCategory? category, CancellationToken cancellationToken)
    {
        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM knowledge_records WHERE ($query='' OR title LIKE $like OR topic LIKE $like OR summary LIKE $like) AND ($category=-1 OR category=$category) ORDER BY is_pinned DESC,updated_at DESC LIMIT 200;";
        var value = query?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("$query", value);
        command.Parameters.AddWithValue("$like", $"%{value}%");
        command.Parameters.AddWithValue("$category", category.HasValue ? (int)category.Value : -1);
        var result = new List<KnowledgeRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new KnowledgeRecord(
                Guid.Parse(reader.GetString(0)),
                (KnowledgeCategory)reader.GetInt32(1),
                reader.GetString(2), reader.GetString(3), reader.GetString(4),
                (KnowledgePrivacyClass)reader.GetInt32(5), reader.GetDouble(6), reader.GetInt32(7) != 0,
                DateTimeOffset.Parse(reader.GetString(8)), DateTimeOffset.Parse(reader.GetString(9)),
                reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
                reader.GetString(11),
                JsonSerializer.Deserialize<IReadOnlyList<KnowledgeSource>>(reader.GetString(12), JsonOptions) ?? []));
        }
        return result;
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

    public async Task<bool> ForgetAsync(Guid id, CancellationToken cancellationToken)
    {
        await KnowledgeSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM knowledge_records WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        var changed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        await retrieval.RemoveSourceAsync(new RetrievalScope(RetrievalScopeKind.Collection, id), "knowledge", id.ToString(), cancellationToken).ConfigureAwait(false);
        return changed;
    }

    public async Task<int> ForgetCategoryAsync(KnowledgeCategory category, CancellationToken cancellationToken)
    {
        var records = await SearchMetadataAsync(null, category, cancellationToken).ConfigureAwait(false);
        foreach (var record in records.Where(record => !record.IsPinned)) await ForgetAsync(record.Id, cancellationToken).ConfigureAwait(false);
        return records.Count(record => !record.IsPinned);
    }
}
