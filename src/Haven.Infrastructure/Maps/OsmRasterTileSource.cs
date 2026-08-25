/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/Maps/OsmRasterTileSource.cs, in the Infrastructure layer.
 * What: Owns OsmRasterTileSource — the ITileSource implementation over OpenStreetMap raster tiles
 *       with an in-memory-free disk cache under {dataDir}/mapcache honouring HTTP caching headers
 *       and the agreed ≥7-day minimum cache lifetime.
 * How: Tiles are stored as {z}/{x}/{y}.png beside a .ttl sidecar recording the server-advertised
 *      lifetime; fresh reads never touch the network, stale-if-error falls back to cached bytes,
 *      writes are atomic (temp file + move), and requests carry Haven's identifying User-Agent.
 * Why: Tile fetching must respect the provider terms — HTTPS only, visible attribution via
 *      MapsAttribution, caching for at least seven days, and no bulk download or prefetch beyond
 *      the caller's current viewport.
 * Maintenance: Viewport-only fetching is the CALLER's duty: GetTileAsync serves exactly one tile
 *              per call and must only be invoked for tiles inside the viewport plus modest
 *              look-ahead. Never add prefetching or background refresh here.
 */

using System.Globalization;
using System.Text;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>ITileSource over OpenStreetMap raster tiles with a local disk cache.</summary>
public sealed class OsmRasterTileSource : ITileSource
{
    /// <summary>Default tile URL template; override with HAVEN_MAPS_TILE_URL_TEMPLATE.</summary>
    public const string DefaultTileUrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    /// <summary>Environment variable overriding the tile URL template ({z}/{x}/{y} placeholders).</summary>
    public const string TileUrlTemplateOverride = "HAVEN_MAPS_TILE_URL_TEMPLATE";

    /// <summary>Cache directory name beneath IAppPaths.DataDirectory.</summary>
    public const string CacheDirectoryName = "mapcache";

    /// <summary>Minimum lifetime a fetched tile stays fresh regardless of short server hints.</summary>
    public static readonly TimeSpan MinimumCacheLifetime = TimeSpan.FromDays(7);

    private const int MaximumZoomLevel = 19;
    private const long MaximumTileBytes = 5L * 1024L * 1024L;

    private readonly HttpClient _client;
    private readonly string _cacheDirectory;

    /// <summary>Creates the tile source over the shared named HttpClient and the app data location.</summary>
    public OsmRasterTileSource(IHttpClientFactory httpClientFactory, IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(appPaths);
        _client = httpClientFactory.CreateClient(OpenStreetMapService.HttpClientName);
        _cacheDirectory = Path.Combine(appPaths.DataDirectory, CacheDirectoryName);
    }

    /// <inheritdoc />
    public string Id => "osm-raster";

    /// <inheritdoc />
    public string Attribution => MapsAttribution.Text;

    /// <summary>Absolute path of the disk cache; shared with the Desktop tile image resolver.</summary>
    public string CacheDirectory => _cacheDirectory;

    /// <inheritdoc />
    public async Task<byte[]?> GetTileAsync(int zoom, int x, int y, CancellationToken cancellationToken)
    {
        if (!IsValidTile(zoom, x, y)) return null;
        var tilePath = BuildCachePath(zoom, x, y);
        var fresh = await ReadFreshOrNullAsync(tilePath, cancellationToken).ConfigureAwait(false);
        if (fresh is not null) return fresh;
        return await FetchAndCacheAsync(zoom, x, y, tilePath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]?> FetchAndCacheAsync(int zoom, int x, int y, string tilePath, CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = ResolveTemplate()
                .Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{x}", x.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{y}", y.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            // The named client also sets this header at registration; setting it per request keeps
            // the provider terms honoured even if the shared registration changes.
            request.Headers.UserAgent.ParseAdd(OpenStreetMapService.UserAgent);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length > MaximumTileBytes) return await ReadStaleOrNullAsync(tilePath, cancellationToken).ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.Length > MaximumTileBytes) return await ReadStaleOrNullAsync(tilePath, cancellationToken).ConfigureAwait(false);

            await WriteCacheAsync(tilePath, bytes, AdvertisedLifetime(response), cancellationToken).ConfigureAwait(false);
            return bytes;
        }
        catch (Exception failure) when ((failure is HttpRequestException or IOException or UriFormatException or TaskCanceledException)
            && !cancellationToken.IsCancellationRequested)
        {
            return await ReadStaleOrNullAsync(tilePath, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<byte[]?> ReadFreshOrNullAsync(string tilePath, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(tilePath);
            if (!info.Exists) return null;
            var ageUtc = DateTime.UtcNow - info.LastWriteTimeUtc;
            if (ageUtc <= await ReadAdvertisedLifetimeAsync(tilePath, cancellationToken).ConfigureAwait(false)) return await File.ReadAllBytesAsync(tilePath, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<byte[]?> ReadStaleOrNullAsync(string tilePath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(tilePath)) return null;
            return await File.ReadAllBytesAsync(tilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<TimeSpan> ReadAdvertisedLifetimeAsync(string tilePath, CancellationToken cancellationToken)
    {
        try
        {
            var sidecarPath = tilePath + ".ttl";
            if (!File.Exists(sidecarPath)) return MinimumCacheLifetime;
            var raw = await File.ReadAllTextAsync(sidecarPath, cancellationToken).ConfigureAwait(false);
            if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0) return MinimumCacheLifetime;
            return TimeSpan.FromSeconds(Math.Max(seconds, MinimumCacheLifetime.TotalSeconds));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return MinimumCacheLifetime;
        }
    }

    private static TimeSpan AdvertisedLifetime(HttpResponseMessage response)
    {
        var maxAge = response.Headers.CacheControl?.MaxAge;
        if (maxAge.HasValue) return maxAge.Value > MinimumCacheLifetime ? maxAge.Value : MinimumCacheLifetime;
        if (response.Content.Headers.Expires is { } expires)
        {
            var untilExpiry = expires - DateTimeOffset.UtcNow;
            if (untilExpiry > MinimumCacheLifetime) return untilExpiry;
        }
        return MinimumCacheLifetime;
    }

    private async Task WriteCacheAsync(string tilePath, byte[] bytes, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(tilePath)!);
            var sidecarPath = tilePath + ".ttl";
            await WriteAtomicallyAsync(sidecarPath, Encoding.ASCII.GetBytes(
                lifetime.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)), cancellationToken).ConfigureAwait(false);
            await WriteAtomicallyAsync(tilePath, bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A failed cache write only costs bandwidth later; the fetch result still returns.
        }
    }

    private static async Task WriteAtomicallyAsync(string targetPath, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporaryPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
            }
        }
    }

    private static string ResolveTemplate()
    {
        var custom = Environment.GetEnvironmentVariable(TileUrlTemplateOverride)?.Trim();
        return !string.IsNullOrWhiteSpace(custom) &&
               custom.StartsWith($"{Uri.UriSchemeHttps}://", StringComparison.OrdinalIgnoreCase) &&
               custom.Contains("{z}", StringComparison.Ordinal) &&
               custom.Contains("{x}", StringComparison.Ordinal) &&
               custom.Contains("{y}", StringComparison.Ordinal)
            ? custom
            : DefaultTileUrlTemplate;
    }

    private string BuildCachePath(int zoom, int x, int y) =>
        Path.Combine(_cacheDirectory, zoom.ToString(CultureInfo.InvariantCulture), x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture) + ".png");

    private static bool IsValidTile(int zoom, int x, int y)
    {
        if (zoom is < 0 or > MaximumZoomLevel) return false;
        var extent = 1 << zoom;
        return x >= 0 && x < extent && y >= 0 && y < extent;
    }
}
