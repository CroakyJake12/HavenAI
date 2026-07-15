namespace Haven.Core;

public enum RetrievalScopeKind
{
    Attachment = 0,
    Conversation = 1,
    Project = 2,
    Subject = 3,
    Collection = 4
}

public sealed record RetrievalScope(RetrievalScopeKind Kind, Guid Id);

public sealed record RetrievalDocument(
    Guid Id,
    RetrievalScopeKind ScopeKind,
    Guid ScopeId,
    string SourceType,
    string SourceId,
    string Title,
    string ContentHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record RetrievalChunk(
    Guid Id,
    Guid DocumentId,
    int Ordinal,
    string Text,
    int StartCharacter,
    int Length,
    IReadOnlyList<float> Embedding,
    IReadOnlyDictionary<string, int> Terms);

public sealed record RetrievalQuery(
    string Text,
    IReadOnlyList<RetrievalScope> Scopes,
    int MaximumResults = 8,
    int TokenBudget = 3_000,
    bool IncludeKeywordSearch = true,
    bool IncludeVectorSearch = true);

public sealed record RetrievalCitation(
    int Number,
    Guid DocumentId,
    Guid ChunkId,
    RetrievalScopeKind ScopeKind,
    Guid ScopeId,
    string Title,
    string SourceType,
    string SourceId,
    string Excerpt,
    int StartCharacter,
    int Length,
    double Score);

public sealed record RetrievalResult(
    string Context,
    IReadOnlyList<RetrievalCitation> Citations,
    int EstimatedTokens,
    string Method);
