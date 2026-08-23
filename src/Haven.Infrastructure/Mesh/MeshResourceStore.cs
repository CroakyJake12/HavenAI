using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Durable Mesh-owned resource backing store. Feature stores require explicit adapters before Mesh may write them.
/// </summary>
public sealed class MeshResourceStore : IMeshResourceMergeService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, MeshResourceSnapshot>? _cache;

    public MeshResourceStore(IAppPaths paths)
    {
        var directory = Path.Combine(paths.DataDirectory, "mesh");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "resources.json");
    }

    public async Task<MeshResourceSnapshot?> GetCurrentAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return state.TryGetValue(Key(resourceType, resourceId), out var value) ? value : null;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> TryApplyAsync(MeshSyncMutation mutation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var key = Key(mutation.ResourceType, mutation.ResourceId);
            if (mutation.Kind == MeshSyncOperationKind.Delete) state.Remove(key);
            else
            {
                state[key] = new MeshResourceSnapshot(
                    mutation.ResourceId, mutation.ResourceType, mutation.Revision, mutation.ContentHash, mutation.OriginDeviceId, mutation.CreatedAt, mutation.Payload);
            }
            await SaveUnsafeAsync(state, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, MeshResourceSnapshot>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null) return _cache;
        if (!File.Exists(_path)) return _cache = new(StringComparer.Ordinal);
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var values = await JsonSerializer.DeserializeAsync<Dictionary<string, MeshResourceSnapshot>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return _cache = values is null ? new(StringComparer.Ordinal) : new(values, StringComparer.Ordinal);
        }
        catch (JsonException ex) { throw new InvalidDataException("Mesh resource state is corrupt and was not overwritten.", ex); }
    }

    private async Task SaveUnsafeAsync(Dictionary<string, MeshResourceSnapshot> state, CancellationToken cancellationToken)
    {
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);
            _cache = state;
        }
        finally { try { File.Delete(temporary); } catch { } }
    }

    private static string Key(string resourceType, Guid resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceType)) throw new ArgumentException("Resource type is required.", nameof(resourceType));
        if (resourceId == Guid.Empty) throw new ArgumentException("Resource ID is required.", nameof(resourceId));
        return resourceType.Trim() + "|" + resourceId.ToString("N");
    }
}
