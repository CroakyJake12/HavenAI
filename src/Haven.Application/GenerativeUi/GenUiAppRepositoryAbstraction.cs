using Haven.Core;
namespace Haven.Application;
public interface IGenUiAppRepository
{
 Task UpsertAsync(GenUiAppDefinition definition,CancellationToken cancellationToken);
 Task<GenUiAppDefinition?> GetAsync(Guid instanceId,CancellationToken cancellationToken);
 Task<IReadOnlyList<GenUiAppDefinition>> GetRecentAsync(int limit,CancellationToken cancellationToken);
 Task DeleteAsync(Guid instanceId,CancellationToken cancellationToken);
}