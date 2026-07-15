using Haven.Core;

namespace Haven.Application;

public interface ITextEmbeddingService
{
    int Dimensions { get; }
    Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken cancellationToken);
}

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

public interface IRetrievalSearchService
{
    Task<RetrievalResult> SearchAsync(RetrievalQuery query, CancellationToken cancellationToken);
}
