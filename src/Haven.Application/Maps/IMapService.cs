/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Maps/IMapService.cs, in the Application layer.
 * What: Owns the Maps capability contracts — IMapService (search, geocoding, routing),
 *       ITileSource (raster map tiles), IMapsSavedPlaceStore (saved places and recent searches) —
 *       plus MapsStoreLogic, the pure normalisation rules those contracts guarantee.
 * How: Public members form the callable contract; implementations live in Infrastructure;
 *      MapsStoreLogic is the single authority for caps, trimming, de-duplication and ordering so
 *      stores, UI and tests cannot drift apart.
 * Why: The Desktop surface and capability actions must depend on capabilities rather than on
 *      OpenStreetMap/OSRM details, while store semantics stay deterministic and testable.
 * Maintenance: Preserve layer boundaries; keep provider terms (attribution, rate limits, caching)
 *              encoded in implementations that honour these contracts, never in callers.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Place search, geocoding and routing for the Maps surface. Implementations enforce the
/// upstream providers' terms (client-side rate limiting, attribution, HTTPS) and return
/// empty/null results for recoverable network failures instead of throwing.
/// </summary>
public interface IMapService
{
    /// <summary>Searches for places matching a free-text query.</summary>
    /// <param name="query">Free-text place query; leading/trailing whitespace is ignored.</param>
    /// <param name="limit">Maximum number of results requested; implementations clamp to their own bounds.</param>
    /// <param name="cancellationToken">Cancels the in-flight request.</param>
    Task<MapSearchResult> SearchAsync(string query, int limit, CancellationToken cancellationToken);

    /// <summary>Resolves the first matching location for a free-text query, or null when nothing matched.</summary>
    Task<GeoPoint?> GeocodeAsync(string query, CancellationToken cancellationToken);

    /// <summary>Resolves a human-readable description for a coordinate, or null when reverse geocoding fails.</summary>
    Task<string?> ReverseGeocodeAsync(GeoPoint point, CancellationToken cancellationToken);

    /// <summary>Computes a route between two points using the requested travel profile, or null when routing fails.</summary>
    Task<MapRoute?> GetRouteAsync(GeoPoint start, GeoPoint end, MapTravelProfile profile, CancellationToken cancellationToken);
}

/// <summary>
/// Source of raster map tiles for the current viewport. Implementations must cache honouring
/// HTTP caching headers with at least the agreed minimum lifetime, send an identifying
/// User-Agent, use HTTPS only, and expose the attribution that surfaces must render.
/// Viewport-only fetching (never bulk download or prefetch beyond the visible range plus modest
/// look-ahead) is the caller's duty and is enforced by the Maps surface, not here.
/// </summary>
public interface ITileSource
{
    /// <summary>Stable identifier of the tile source, e.g. "osm-raster".</summary>
    string Id { get; }

    /// <summary>Attribution text that must stay visible whenever this source's tiles are shown.</summary>
    string Attribution { get; }

    /// <summary>Returns the encoded image bytes for one tile, or null when it cannot be provided.</summary>
    Task<byte[]?> GetTileAsync(int zoom, int x, int y, CancellationToken cancellationToken);
}

/// <summary>Persisted saved places and recent search queries for Maps, stored locally only.</summary>
public interface IMapsSavedPlaceStore
{
    /// <summary>Returns saved places, newest first, capped by <see cref="MapsStoreLogic.MaxSavedPlaces"/>.</summary>
    Task<IReadOnlyList<SavedMapPlace>> GetSavedPlacesAsync(CancellationToken cancellationToken);

    /// <summary>Saves or updates a place by id, preserving the cap and ordering guarantees.</summary>
    Task SaveAsync(SavedMapPlace place, CancellationToken cancellationToken);

    /// <summary>Removes the saved place with the given id; unknown ids are ignored.</summary>
    Task RemoveAsync(string id, CancellationToken cancellationToken);

    /// <summary>Returns recent searches, newest first, capped by <see cref="MapsStoreLogic.MaxRecentSearches"/>.</summary>
    Task<IReadOnlyList<string>> GetRecentSearchesAsync(CancellationToken cancellationToken);

    /// <summary>Records a successful search query as the most recent entry, preserving the cap.</summary>
    Task RecordSearchAsync(string query, CancellationToken cancellationToken);
}

/// <summary>
/// Pure normalisation rules shared by every saved-place/recent-search implementation:
/// field trimming, coordinate wrapping, id de-duplication, ordering and caps. Kept side-effect
/// free so Infrastructure persistence and tests exercise exactly the same rules.
/// </summary>
public static class MapsStoreLogic
{
    /// <summary>Maximum number of saved places retained.</summary>
    public const int MaxSavedPlaces = 200;

    /// <summary>Maximum number of recent search queries retained.</summary>
    public const int MaxRecentSearches = 20;

    /// <summary>
    /// Normalises a saved-place list: trims fields, blanks out empty notes and names (falling back
    /// to the id), wraps longitudes into (-180, 180], clamps latitudes, keeps the newest entry per
    /// id, orders newest first, and caps at <see cref="MaxSavedPlaces"/>.
    /// </summary>
    public static IReadOnlyList<SavedMapPlace> NormaliseSavedPlaces(IEnumerable<SavedMapPlace?>? places)
    {
        if (places is null) return [];
        var latestById = new Dictionary<string, SavedMapPlace>(StringComparer.Ordinal);
        foreach (var candidate in places)
        {
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.Id)) continue;
            var normalised = Normalise(candidate);
            if (!latestById.TryGetValue(normalised.Id, out var existing) || normalised.SavedAt > existing.SavedAt)
                latestById[normalised.Id] = normalised;
        }

        return latestById.Values
            .OrderByDescending(place => place.SavedAt)
            .ThenBy(place => place.Id, StringComparer.Ordinal)
            .Take(MaxSavedPlaces)
            .ToArray();
    }

    /// <summary>
    /// Normalises a recent-search history given oldest-first input: trims entries, drops empties,
    /// de-duplicates case-insensitively keeping the most recent occurrence, returns newest first,
    /// and caps at <see cref="MaxRecentSearches"/>.
    /// </summary>
    public static IReadOnlyList<string> NormaliseRecentSearches(IEnumerable<string?>? searches)
    {
        if (searches is null) return [];
        var materialised = searches
            .Where(search => !string.IsNullOrWhiteSpace(search))
            .Select(search => search!.Trim())
            .ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newestFirst = new List<string>(Math.Min(materialised.Length, MaxRecentSearches));
        for (var index = materialised.Length - 1; index >= 0 && newestFirst.Count < MaxRecentSearches; index--)
        {
            if (seen.Add(materialised[index])) newestFirst.Add(materialised[index]);
        }
        return newestFirst;
    }

    private static SavedMapPlace Normalise(SavedMapPlace place)
    {
        var id = place.Id.Trim();
        var displayName = string.IsNullOrWhiteSpace(place.DisplayName) ? id : place.DisplayName.Trim();
        var note = string.IsNullOrWhiteSpace(place.Note) ? null : place.Note!.Trim();
        return new SavedMapPlace(
            id,
            displayName,
            note,
            new GeoPoint(Math.Clamp(place.Location.Latitude, -90d, 90d), WrapLongitude(place.Location.Longitude)),
            place.SavedAt);
    }

    private static double WrapLongitude(double longitudeDegrees)
    {
        var wrapped = (longitudeDegrees + 180d) % 360d;
        if (wrapped <= 0d) wrapped += 360d;
        return wrapped - 180d;
    }
}
