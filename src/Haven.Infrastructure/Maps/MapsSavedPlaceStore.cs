/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/Maps/MapsSavedPlaceStore.cs, in the Infrastructure layer.
 * What: Owns MapsSavedPlaceStore — the JSON-file IMapsSavedPlaceStore persisted under
 *       {dataDir}/maps/saved-places.json with recent searches capped at 20 and saved places at 200.
 * How: Every mutation loads, applies MapsStoreLogic normalisation (caps, trimming, ordering) and
 *      writes atomically — the previous file is copied to .bak before the temp-file move — guarded
 *      by a semaphore so concurrent mutations serialise; corrupt files fall back to defaults.
 * Why: Saved places and recent searches are local user data; persistence details stay in
 *      Infrastructure while caps and ordering rules stay shared through Application logic.
 * Maintenance: Keep the file format forward-compatible (unknown fields ignored); never recycle
 *              saved-place ids; preserve the .bak-before-overwrite behaviour.
 */

using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Persisted document for saved map places and recent search queries.</summary>
/// <param name="SavedPlaces">Saved places, newest first.</param>
/// <param name="RecentSearches">Recent queries, newest first.</param>
public sealed record MapsSavedPlaceDocument(
    [property: JsonPropertyName("savedPlaces")] IReadOnlyList<SavedMapPlace> SavedPlaces,
    [property: JsonPropertyName("recentSearches")] IReadOnlyList<string> RecentSearches);

/// <summary>JSON-file store of saved map places and recent searches beneath {dataDirectory}/maps.</summary>
public sealed class MapsSavedPlaceStore : IMapsSavedPlaceStore
{
    /// <summary>File name of the store within the maps data directory.</summary>
    public const string StoreFileName = "saved-places.json";

    private static readonly JsonSerializerOptions SerialiserOptions = new() { WriteIndented = true };

    private readonly string _storePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates the store under the application data directory.</summary>
    public MapsSavedPlaceStore(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _storePath = Path.Combine(appPaths.DataDirectory, "maps", StoreFileName);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SavedMapPlace>> GetSavedPlacesAsync(CancellationToken cancellationToken)
    {
        var document = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return document.SavedPlaces;
    }

    /// <inheritdoc />
    public async Task SaveAsync(SavedMapPlace place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(place);
        var savedPlaceId = place.Id.Trim();
        await MutateAsync(current => new MapsSavedPlaceDocument(
            MapsStoreLogic.NormaliseSavedPlaces(current.SavedPlaces
                .Where(existing => !existing.Id.Equals(savedPlaceId, StringComparison.Ordinal))
                .Append(place)),
            current.RecentSearches), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var trimmedId = id.Trim();
        await MutateAsync(current => new MapsSavedPlaceDocument(
            MapsStoreLogic.NormaliseSavedPlaces(current.SavedPlaces
                .Where(existing => !existing.Id.Equals(trimmedId, StringComparison.Ordinal))),
            current.RecentSearches), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRecentSearchesAsync(CancellationToken cancellationToken)
    {
        var document = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return document.RecentSearches;
    }

    /// <inheritdoc />
    public async Task RecordSearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        await MutateAsync(current => new MapsSavedPlaceDocument(
            current.SavedPlaces,
            MapsStoreLogic.NormaliseRecentSearches(current.RecentSearches.Append(query))),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task MutateAsync(Func<MapsSavedPlaceDocument, MapsSavedPlaceDocument> mutate, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var mutated = mutate(current);
            var updated = new MapsSavedPlaceDocument(
                MapsStoreLogic.NormaliseSavedPlaces(mutated.SavedPlaces),
                MapsStoreLogic.NormaliseRecentSearches(mutated.RecentSearches));
            await WriteAtomicWithBackupAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<MapsSavedPlaceDocument> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<MapsSavedPlaceDocument> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_storePath)) return new MapsSavedPlaceDocument([], []);
            await using var stream = File.OpenRead(_storePath);
            var document = await JsonSerializer.DeserializeAsync<MapsSavedPlaceDocument>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document is null) return new MapsSavedPlaceDocument([], []);
            return new MapsSavedPlaceDocument(
                MapsStoreLogic.NormaliseSavedPlaces(document.SavedPlaces),
                MapsStoreLogic.NormaliseRecentSearches(document.RecentSearches));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return new MapsSavedPlaceDocument([], []);
        }
    }

    private async Task WriteAtomicWithBackupAsync(MapsSavedPlaceDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_storePath);
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        try
        {
            if (File.Exists(_storePath)) File.Copy(_storePath, _storePath + ".bak", overwrite: true);
            await using var stream = File.Create(_storePath + ".tmp");
            await JsonSerializer.SerializeAsync(stream, document, SerialiserOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            TryDeleteTemporary();
            throw;
        }
        File.Move(_storePath + ".tmp", _storePath, overwrite: true);

        void TryDeleteTemporary()
        {
            try
            {
                if (File.Exists(_storePath + ".tmp")) File.Delete(_storePath + ".tmp");
            }
            catch (IOException)
            {
            }
        }
    }
}
