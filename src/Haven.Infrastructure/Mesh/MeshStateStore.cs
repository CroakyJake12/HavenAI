using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class MeshStateStore : IMeshStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MeshStateStore(IAppPaths paths)
    {
        var directory = Path.Combine(paths.DataDirectory, "mesh");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "mesh-state.json");
    }

    public async Task<MeshPersistentState> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return MeshPersistentState.Empty;
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var state = await JsonSerializer.DeserializeAsync<MeshPersistentState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return state ?? MeshPersistentState.Empty;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Mesh state is corrupt and was not silently reset.", ex);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(MeshPersistentState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                    await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
                await using (var verify = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    _ = await JsonSerializer.DeserializeAsync<MeshPersistentState>(verify, JsonOptions, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("Mesh state verification read returned no state.");
                File.Move(temporary, _path, overwrite: true);
            }
            finally { try { File.Delete(temporary); } catch { } }
        }
        finally { _gate.Release(); }
    }
}
