using Haven.Core;

namespace Haven.Application;

public sealed record LocalShareHandle(
    Guid SessionId,
    Uri Address,
    DateTimeOffset ExpiresAt,
    string Notice);

public interface ILocalConversationShareService : IAsyncDisposable
{
    Task<LocalShareHandle> StartAsync(Guid conversationId, TimeSpan duration, CancellationToken cancellationToken);
    Task StopAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<SharedSession?> GetActiveAsync(Guid conversationId, CancellationToken cancellationToken);
}
