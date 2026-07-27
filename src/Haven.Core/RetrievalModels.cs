/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/RetrievalModels.cs, in the dependency-free Core layer, where shared domain models and rules live.
 * What: This file owns RetrievalScopeKind, RetrievalScope, RetrievalDocument, RetrievalChunk, RetrievalQuery, RetrievalCitation, RetrievalResult. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: This code stays free of UI and storage dependencies so the same rule or data shape can be reused and tested everywhere.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Core;

/// <summary>
/// Lists the supported retrieval scope kind values used to make state explicit and type-safe.
/// </summary>
public enum RetrievalScopeKind
{
    Attachment = 0,
    Conversation = 1,
    Project = 2,
    Subject = 3,
    Collection = 4
}

/// <summary>
/// Represents retrieval scope and keeps its related state and behavior together.
/// </summary>
public sealed record RetrievalScope(RetrievalScopeKind Kind, Guid Id);

/// <summary>
/// Represents retrieval document and keeps its related state and behavior together.
/// </summary>
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

/// <summary>
/// Represents retrieval chunk and keeps its related state and behavior together.
/// </summary>
public sealed record RetrievalChunk(
    Guid Id,
    Guid DocumentId,
    int Ordinal,
    string Text,
    int StartCharacter,
    int Length,
    IReadOnlyList<float> Embedding,
    IReadOnlyDictionary<string, int> Terms);

/// <summary>
/// Represents retrieval query and keeps its related state and behavior together.
/// </summary>
public sealed record RetrievalQuery(
    string Text,
    IReadOnlyList<RetrievalScope> Scopes,
    int MaximumResults = 8,
    int TokenBudget = 3_000,
    bool IncludeKeywordSearch = true,
    bool IncludeVectorSearch = true);

/// <summary>
/// Represents retrieval citation and keeps its related state and behavior together.
/// </summary>
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

/// <summary>
/// Represents retrieval result and keeps its related state and behavior together.
/// </summary>
public sealed record RetrievalResult(
    string Context,
    IReadOnlyList<RetrievalCitation> Citations,
    int EstimatedTokens,
    string Method);
