using Haven.Core;

namespace Haven.Application;

/// <summary>Caches installed-model discovery so a network inventory request does not block every chat turn.</summary>
public sealed class ChatModelInventoryCache(IOllamaClient ollama, TimeSpan? lifetime = null)
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly TimeSpan _lifetime = lifetime ?? TimeSpan.FromSeconds(30);
    private IReadOnlyList<ModelDescriptor> _models = [];
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<ModelDescriptor>> GetAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh && DateTimeOffset.UtcNow < _expiresAt)
            return _models;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && DateTimeOffset.UtcNow < _expiresAt)
                return _models;

            var discovered = await ollama.GetModelsAsync(cancellationToken).ConfigureAwait(false);
            _models = discovered.ToArray();
            _expiresAt = DateTimeOffset.UtcNow + _lifetime;
            return _models;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Invalidate() => _expiresAt = DateTimeOffset.MinValue;
}