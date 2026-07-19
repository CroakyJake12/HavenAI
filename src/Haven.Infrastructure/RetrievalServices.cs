/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/RetrievalServices.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns RetrievalSchema, LocalHashEmbeddingService, RetrievalIndexService, Segment, CandidateChunk, ScoredChunk. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Represents retrieval schema and keeps its related state and behavior together.
/// </summary>
internal static class RetrievalSchema
{
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// Performs ensure asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public static async Task EnsureAsync(ISqliteConnectionFactory factory, CancellationToken cancellationToken)
    {
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS retrieval_documents(
                    id TEXT PRIMARY KEY,
                    scope_kind INTEGER NOT NULL,
                    scope_id TEXT NOT NULL,
                    source_type TEXT NOT NULL,
                    source_id TEXT NOT NULL,
                    title TEXT NOT NULL,
                    content_hash TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ix_retrieval_documents_source
                    ON retrieval_documents(scope_kind,scope_id,source_type,source_id);
                CREATE INDEX IF NOT EXISTS ix_retrieval_documents_scope
                    ON retrieval_documents(scope_kind,scope_id,updated_at);

                CREATE TABLE IF NOT EXISTS retrieval_chunks(
                    id TEXT PRIMARY KEY,
                    document_id TEXT NOT NULL REFERENCES retrieval_documents(id) ON DELETE CASCADE,
                    ordinal INTEGER NOT NULL,
                    text TEXT NOT NULL,
                    start_character INTEGER NOT NULL,
                    length INTEGER NOT NULL,
                    embedding_json TEXT NOT NULL,
                    terms_json TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ix_retrieval_chunks_ordinal ON retrieval_chunks(document_id,ordinal);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using var migration = connection.CreateCommand();
            migration.Transaction = transaction;
            migration.CommandText = "INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(10,$now);";
            migration.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await migration.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}

/// <summary>
/// Represents local hash embedding service and keeps its related state and behavior together.
/// </summary>
public sealed class LocalHashEmbeddingService : ITextEmbeddingService
{
    /// <summary>
    /// Stores token pattern locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Regex TokenPattern = new("[\\p{L}\\p{N}_-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    /// <summary>
    /// Gets or updates dimensions, the bindable or domain state represented by this property.
    /// </summary>
    public int Dimensions => 384;

    /// <summary>
    /// Performs embed asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vector = new float[Dimensions];
        foreach (Match match in TokenPattern.Matches(text.ToLowerInvariant()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = match.Value;
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var index = BitConverter.ToUInt32(bytes, 0) % (uint)Dimensions;
            var sign = (bytes[4] & 1) == 0 ? 1f : -1f;
            var weight = 1f + MathF.Min(2f, token.Length / 8f);
            vector[index] += sign * weight;
            if (token.Length >= 5)
            {
                var second = BitConverter.ToUInt32(bytes, 8) % (uint)Dimensions;
                vector[second] += sign * 0.35f;
            }
        }
        Normalize(vector);
        return Task.FromResult<IReadOnlyList<float>>(vector);
    }

    /// <summary>
    /// Performs the normalize step owned by this component.
    /// </summary>
    private static void Normalize(float[] vector)
    {
        var sum = vector.Sum(value => value * value);
        if (sum <= 0) return;
        var norm = MathF.Sqrt(sum);
        for (var index = 0; index < vector.Length; index++) vector[index] /= norm;
    }
}

/// <summary>
/// Represents retrieval index service and keeps its related state and behavior together.
/// </summary>
public sealed class RetrievalIndexService(
    ISqliteConnectionFactory factory,
    ITextEmbeddingService embeddings) : IRetrievalIndexService, IRetrievalSearchService
{
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    /// <summary>
    /// Stores token pattern locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Regex TokenPattern = new("[\\p{L}\\p{N}_-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    /// <summary>
    /// Stores chunk target locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int ChunkTarget = 1_500;
    /// <summary>
    /// Stores chunk overlap locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int ChunkOverlap = 220;

    /// <summary>
    /// Performs index text asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<RetrievalDocument> IndexTextAsync(
        RetrievalScope scope,
        string sourceType,
        string sourceId,
        string title,
        string text,
        CancellationToken cancellationToken)
    {
        ValidateSource(scope, sourceType, sourceId, title);
        await RetrievalSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        var normalized = NormalizeText(text);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        var existing = await FindDocumentAsync(scope, sourceType, sourceId, cancellationToken).ConfigureAwait(false);
        if (existing is not null && existing.ContentHash.Equals(hash, StringComparison.OrdinalIgnoreCase)) return existing;

        var now = DateTimeOffset.UtcNow;
        var document = existing is null
            ? new RetrievalDocument(Guid.NewGuid(), scope.Kind, scope.Id, sourceType.Trim(), sourceId.Trim(), title.Trim(), hash, now, now)
            : existing with { Title = title.Trim(), ContentHash = hash, UpdatedAt = now };
        var chunks = new List<RetrievalChunk>();
        foreach (var segment in Split(normalized))
        {
            cancellationToken.ThrowIfCancellationRequested();
            chunks.Add(new RetrievalChunk(
                Guid.NewGuid(), document.Id, segment.Ordinal, segment.Text, segment.Start, segment.Text.Length,
                await embeddings.EmbedAsync(segment.Text, cancellationToken).ConfigureAwait(false),
                CountTerms(segment.Text)));
        }

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO retrieval_documents(id,scope_kind,scope_id,source_type,source_id,title,content_hash,created_at,updated_at)
                VALUES($id,$scopeKind,$scopeId,$sourceType,$sourceId,$title,$hash,$createdAt,$updatedAt)
                ON CONFLICT(scope_kind,scope_id,source_type,source_id) DO UPDATE SET
                  title=excluded.title,content_hash=excluded.content_hash,updated_at=excluded.updated_at;
                """;
            AddDocumentParameters(upsert, document);
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM retrieval_chunks WHERE document_id=$documentId;";
            clear.Parameters.AddWithValue("$documentId", document.Id.ToString());
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        foreach (var chunk in chunks)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO retrieval_chunks(id,document_id,ordinal,text,start_character,length,embedding_json,terms_json)
                VALUES($id,$documentId,$ordinal,$text,$start,$length,$embedding,$terms);
                """;
            insert.Parameters.AddWithValue("$id", chunk.Id.ToString());
            insert.Parameters.AddWithValue("$documentId", chunk.DocumentId.ToString());
            insert.Parameters.AddWithValue("$ordinal", chunk.Ordinal);
            insert.Parameters.AddWithValue("$text", chunk.Text);
            insert.Parameters.AddWithValue("$start", chunk.StartCharacter);
            insert.Parameters.AddWithValue("$length", chunk.Length);
            insert.Parameters.AddWithValue("$embedding", JsonSerializer.Serialize(chunk.Embedding, JsonOptions));
            insert.Parameters.AddWithValue("$terms", JsonSerializer.Serialize(chunk.Terms, JsonOptions));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return document;
    }

    /// <summary>
    /// Performs remove source asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task RemoveSourceAsync(RetrievalScope scope, string sourceType, string sourceId, CancellationToken cancellationToken)
    {
        await RetrievalSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM retrieval_documents WHERE scope_kind=$scopeKind AND scope_id=$scopeId AND source_type=$sourceType AND source_id=$sourceId;";
        command.Parameters.AddWithValue("$scopeKind", (int)scope.Kind);
        command.Parameters.AddWithValue("$scopeId", scope.Id.ToString());
        command.Parameters.AddWithValue("$sourceType", sourceType.Trim());
        command.Parameters.AddWithValue("$sourceId", sourceId.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves documents async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<RetrievalDocument>> GetDocumentsAsync(RetrievalScope scope, CancellationToken cancellationToken)
    {
        await RetrievalSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM retrieval_documents WHERE scope_kind=$scopeKind AND scope_id=$scopeId ORDER BY updated_at DESC;";
        command.Parameters.AddWithValue("$scopeKind", (int)scope.Kind);
        command.Parameters.AddWithValue("$scopeId", scope.Id.ToString());
        return await ReadDocumentsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs search asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<RetrievalResult> SearchAsync(RetrievalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.Text) || query.Scopes.Count == 0)
            return new RetrievalResult(string.Empty, [], 0, "No retrieval scope or query was supplied.");
        await RetrievalSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        var candidates = await LoadCandidatesAsync(query.Scopes, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0) return new RetrievalResult(string.Empty, [], 0, "No indexed content was available in the selected scopes.");

        var queryTerms = CountTerms(query.Text);
        var queryVector = query.IncludeVectorSearch
            ? await embeddings.EmbedAsync(query.Text, cancellationToken).ConfigureAwait(false)
            : Array.Empty<float>();
        var phrase = NormalizeText(query.Text).ToLowerInvariant();
        var scored = candidates.Select(item =>
        {
            var keyword = query.IncludeKeywordSearch ? KeywordScore(queryTerms, item.Chunk.Terms, candidates.Count) : 0;
            var vector = query.IncludeVectorSearch ? Cosine(queryVector, item.Chunk.Embedding) : 0;
            var phraseBoost = phrase.Length >= 5 && item.Chunk.Text.Contains(phrase, StringComparison.OrdinalIgnoreCase) ? 0.35 : 0;
            var titleBoost = item.Document.Title.Contains(query.Text, StringComparison.OrdinalIgnoreCase) ? 0.18 : 0;
            var score = keyword * 0.56 + Math.Max(0, vector) * 0.34 + phraseBoost + titleBoost;
            return new ScoredChunk(item.Document, item.Chunk, score);
        })
        .Where(item => item.Score > 0)
        .OrderByDescending(item => item.Score)
        .ThenByDescending(item => item.Document.UpdatedAt)
        .ThenBy(item => item.Document.Title, StringComparer.OrdinalIgnoreCase)
        .ToArray();

        var selected = new List<ScoredChunk>();
        var usedDocuments = new Dictionary<Guid, int>();
        var estimatedTokens = 0;
        foreach (var item in scored)
        {
            if (selected.Count >= Math.Clamp(query.MaximumResults, 1, 50)) break;
            var chunkTokens = EstimateTokens(item.Chunk.Text);
            if (estimatedTokens + chunkTokens > Math.Max(128, query.TokenBudget)) continue;
            if (usedDocuments.TryGetValue(item.Document.Id, out var count) && count >= 3) continue;
            selected.Add(item);
            usedDocuments[item.Document.Id] = count + 1;
            estimatedTokens += chunkTokens;
        }

        var citations = selected.Select((item, index) => new RetrievalCitation(
            index + 1,
            item.Document.Id,
            item.Chunk.Id,
            item.Document.ScopeKind,
            item.Document.ScopeId,
            item.Document.Title,
            item.Document.SourceType,
            item.Document.SourceId,
            Excerpt(item.Chunk.Text, 520),
            item.Chunk.StartCharacter,
            item.Chunk.Length,
            item.Score)).ToArray();
        var context = string.Join("\n\n", citations.Select(citation =>
            $"[source {citation.Number}] {citation.Title} ({citation.SourceType}:{citation.SourceId}, characters {citation.StartCharacter}-{citation.StartCharacter + citation.Length})\n{citation.Excerpt}"));
        var method = query.IncludeKeywordSearch && query.IncludeVectorSearch
            ? "Hybrid local keyword + hashing-vector retrieval with deterministic reranking."
            : query.IncludeVectorSearch
                ? "Local hashing-vector retrieval."
                : "Local keyword retrieval.";
        return new RetrievalResult(context, citations, estimatedTokens, method);
    }

    /// <summary>
    /// Performs find document asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<RetrievalDocument?> FindDocumentAsync(RetrievalScope scope, string sourceType, string sourceId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM retrieval_documents WHERE scope_kind=$scopeKind AND scope_id=$scopeId AND source_type=$sourceType AND source_id=$sourceId LIMIT 1;";
        command.Parameters.AddWithValue("$scopeKind", (int)scope.Kind);
        command.Parameters.AddWithValue("$scopeId", scope.Id.ToString());
        command.Parameters.AddWithValue("$sourceType", sourceType.Trim());
        command.Parameters.AddWithValue("$sourceId", sourceId.Trim());
        return (await ReadDocumentsAsync(command, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    /// <summary>
    /// Performs load candidates asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<IReadOnlyList<CandidateChunk>> LoadCandidatesAsync(IReadOnlyList<RetrievalScope> scopes, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<CandidateChunk>();
        foreach (var scope in scopes.Distinct())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT d.*,c.id AS chunk_id,c.ordinal,c.text,c.start_character,c.length,c.embedding_json,c.terms_json
                  FROM retrieval_documents d JOIN retrieval_chunks c ON c.document_id=d.id
                 WHERE d.scope_kind=$scopeKind AND d.scope_id=$scopeId
                 ORDER BY d.updated_at DESC,c.ordinal LIMIT 4000;
                """;
            command.Parameters.AddWithValue("$scopeKind", (int)scope.Kind);
            command.Parameters.AddWithValue("$scopeId", scope.Id.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = ReadDocument(reader);
                var embedding = JsonSerializer.Deserialize<float[]>(reader.GetString(reader.GetOrdinal("embedding_json")), JsonOptions) ?? [];
                var terms = JsonSerializer.Deserialize<Dictionary<string, int>>(reader.GetString(reader.GetOrdinal("terms_json")), JsonOptions)
                            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                result.Add(new CandidateChunk(document, new RetrievalChunk(
                    Guid.Parse(reader.GetString(reader.GetOrdinal("chunk_id"))),
                    document.Id,
                    reader.GetInt32(reader.GetOrdinal("ordinal")),
                    reader.GetString(reader.GetOrdinal("text")),
                    reader.GetInt32(reader.GetOrdinal("start_character")),
                    reader.GetInt32(reader.GetOrdinal("length")),
                    embedding,
                    terms)));
            }
        }
        return result;
    }

    /// <summary>
    /// Performs read documents asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<IReadOnlyList<RetrievalDocument>> ReadDocumentsAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var result = new List<RetrievalDocument>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadDocument(reader));
        return result;
    }

    /// <summary>
    /// Performs the read document step owned by this component.
    /// </summary>
    private static RetrievalDocument ReadDocument(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
        (RetrievalScopeKind)reader.GetInt32(reader.GetOrdinal("scope_kind")),
        Guid.Parse(reader.GetString(reader.GetOrdinal("scope_id"))),
        reader.GetString(reader.GetOrdinal("source_type")),
        reader.GetString(reader.GetOrdinal("source_id")),
        reader.GetString(reader.GetOrdinal("title")),
        reader.GetString(reader.GetOrdinal("content_hash")),
        DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at")), System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at")), System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Performs the split step owned by this component.
    /// </summary>
    private static IEnumerable<Segment> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var start = 0;
        var ordinal = 0;
        while (start < text.Length)
        {
            var desiredEnd = Math.Min(text.Length, start + ChunkTarget);
            var end = desiredEnd;
            if (desiredEnd < text.Length)
            {
                var paragraph = text.LastIndexOf("\n\n", desiredEnd, Math.Min(500, desiredEnd - start), StringComparison.Ordinal);
                var sentence = text.LastIndexOfAny(['.', '!', '?', '\n'], desiredEnd - 1, Math.Min(350, desiredEnd - start));
                if (paragraph > start + 600) end = paragraph + 2;
                else if (sentence > start + 600) end = sentence + 1;
            }
            var value = text[start..end].Trim();
            if (value.Length > 0) yield return new Segment(ordinal++, start, value);
            if (end >= text.Length) break;
            start = Math.Max(start + 1, end - ChunkOverlap);
        }
    }

    /// <summary>
    /// Performs the count terms step owned by this component.
    /// </summary>
    private static IReadOnlyDictionary<string, int> CountTerms(string text)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in TokenPattern.Matches(text.ToLowerInvariant()))
        {
            var token = match.Value;
            if (token.Length < 2 || StopWords.Contains(token)) continue;
            result[token] = result.GetValueOrDefault(token) + 1;
        }
        return result;
    }

    /// <summary>
    /// Performs the keyword score step owned by this component.
    /// </summary>
    private static double KeywordScore(IReadOnlyDictionary<string, int> query, IReadOnlyDictionary<string, int> document, int corpusSize)
    {
        if (query.Count == 0 || document.Count == 0) return 0;
        double score = 0;
        foreach (var term in query)
        {
            if (!document.TryGetValue(term.Key, out var frequency)) continue;
            var saturation = frequency / (frequency + 1.2);
            var queryWeight = 1 + Math.Log(1 + term.Value);
            score += saturation * queryWeight * (1 + Math.Log(1 + corpusSize) / 20);
        }
        return score / Math.Max(1, query.Count);
    }

    /// <summary>
    /// Performs the cosine step owned by this component.
    /// </summary>
    private static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count == 0 || right.Count == 0 || left.Count != right.Count) return 0;
        double dot = 0;
        for (var index = 0; index < left.Count; index++) dot += left[index] * right[index];
        return dot;
    }

    /// <summary>
    /// Performs the add document parameters step owned by this component.
    /// </summary>
    private static void AddDocumentParameters(SqliteCommand command, RetrievalDocument document)
    {
        command.Parameters.AddWithValue("$id", document.Id.ToString());
        command.Parameters.AddWithValue("$scopeKind", (int)document.ScopeKind);
        command.Parameters.AddWithValue("$scopeId", document.ScopeId.ToString());
        command.Parameters.AddWithValue("$sourceType", document.SourceType);
        command.Parameters.AddWithValue("$sourceId", document.SourceId);
        command.Parameters.AddWithValue("$title", document.Title);
        command.Parameters.AddWithValue("$hash", document.ContentHash);
        command.Parameters.AddWithValue("$createdAt", document.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", document.UpdatedAt.ToString("O"));
    }

    /// <summary>
    /// Validates source before it crosses the next trust or persistence boundary.
    /// </summary>
    private static void ValidateSource(RetrievalScope scope, string sourceType, string sourceId, string title)
    {
        if (scope.Id == Guid.Empty) throw new ArgumentException("Retrieval scope identifier is required.", nameof(scope));
        if (string.IsNullOrWhiteSpace(sourceType) || sourceType.Length > 80) throw new ArgumentException("A short source type is required.", nameof(sourceType));
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > 500) throw new ArgumentException("A source identifier is required.", nameof(sourceId));
        if (string.IsNullOrWhiteSpace(title) || title.Length > 500) throw new ArgumentException("A source title is required.", nameof(title));
    }

    /// <summary>
    /// Performs the normalize text step owned by this component.
    /// </summary>
    private static string NormalizeText(string value) => value.Replace("\0", string.Empty, StringComparison.Ordinal).ReplaceLineEndings("\n").Trim();
    /// <summary>
    /// Performs the estimate tokens step owned by this component.
    /// </summary>
    private static int EstimateTokens(string value) => Math.Max(1, (int)Math.Ceiling(Encoding.UTF8.GetByteCount(value) / 4d));
    /// <summary>
    /// Performs the excerpt step owned by this component.
    /// </summary>
    private static string Excerpt(string value, int maximum) => value.Length <= maximum ? value : value[..maximum].TrimEnd() + "…";

    /// <summary>
    /// Stores stop words locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "that", "with", "this", "from", "have", "are", "was", "were", "into", "your", "you", "but", "not", "can", "will", "about", "what", "when", "where", "which"
    };

    /// <summary>
    /// Represents segment and keeps its related state and behavior together.
    /// </summary>
    private sealed record Segment(int Ordinal, int Start, string Text);
    /// <summary>
    /// Represents candidate chunk and keeps its related state and behavior together.
    /// </summary>
    private sealed record CandidateChunk(RetrievalDocument Document, RetrievalChunk Chunk);
    /// <summary>
    /// Represents scored chunk and keeps its related state and behavior together.
    /// </summary>
    private sealed record ScoredChunk(RetrievalDocument Document, RetrievalChunk Chunk, double Score);
}
