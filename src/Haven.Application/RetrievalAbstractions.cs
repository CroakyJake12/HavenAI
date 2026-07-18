/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/RetrievalAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ITextEmbeddingService, IRetrievalIndexService, IRetrievalSearchService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Defines the i text embedding service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface ITextEmbeddingService
{
    int Dimensions { get; }
    Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the i retrieval index service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IRetrievalIndexService
{
    Task<RetrievalDocument> IndexTextAsync(
        RetrievalScope scope,
        string sourceType,
        string sourceId,
        string title,
        string text,
        CancellationToken cancellationToken);

    Task RemoveSourceAsync(RetrievalScope scope, string sourceType, string sourceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RetrievalDocument>> GetDocumentsAsync(RetrievalScope scope, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the i retrieval search service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IRetrievalSearchService
{
    Task<RetrievalResult> SearchAsync(RetrievalQuery query, CancellationToken cancellationToken);
}
