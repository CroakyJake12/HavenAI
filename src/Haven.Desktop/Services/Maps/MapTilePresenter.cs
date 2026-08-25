/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/Maps/MapTilePresenter.cs, in the Desktop composition layer.
 * What: Owns the pure map presentation maths — MapTilePresenter (Web-Mercator facade),
 *       MapTileId, ViewportState (centre, zoom, pixel size, visible tile range with at most one
 *       tile of look-ahead) — plus MapTileImageResolver, the capability-aware backend image
 *       resolver that decodes "havenmaptile://" sources from the local tile cache.
 * How: All projection maths delegates to WebMercatorProjection in Haven.Application; ViewportState
 *      converts a centre/zoom/viewport into concrete tile ids and screen positions;
 *      MapTileImageResolver implements IHavenAvaloniaImageResolver so the standard scene renderer
 *      can display cached raster tiles without any network access.
 * Why: Presentation maths must be side-effect free and provider-agnostic, while image decoding
 *      stays an explicit host-supplied capability per HavenUI's resolver contract.
 * Maintenance: Keep this file free of I/O beyond reading the cache directory handed to the
 *              resolver; fetching belongs to ITileSource and remains the caller's viewport-only duty.
 */

using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.Infrastructure;
using Haven.Core;

namespace Haven.Desktop.Services.Maps;

/// <summary>Presentation facade over Web-Mercator tile maths at the 256 px tile size.</summary>
public static class MapTilePresenter
{
    /// <summary>Edge length of one raster map tile in device-independent pixels.</summary>
    public const double PixelsPerTile = WebMercatorProjection.PixelsPerTile;

    /// <summary>Converts decimal degrees to fractional slippy-map tile coordinates.</summary>
    public static (double TileX, double TileY) LatLonToTileXY(int zoomLevel, double latitudeDegrees, double longitudeDegrees) =>
        WebMercatorProjection.LatLonToTileXY(zoomLevel, latitudeDegrees, longitudeDegrees);

    /// <summary>Converts fractional slippy-map tile coordinates back to decimal degrees.</summary>
    public static GeoPoint TileXYToLatLon(int zoomLevel, double tileX, double tileY) =>
        WebMercatorProjection.TileXYToLatLon(zoomLevel, tileX, tileY);

    /// <summary>Stable source-key scheme shared by the scene's tile images and the resolver.</summary>
    public static string ToSourceKey(MapTileId tileId) =>
        $"{MapTileImageResolver.SourceSchemePrefix}{tileId.Zoom}/{tileId.X}/{tileId.Y}";
}

/// <summary>Identifies one raster tile in the slippy-map tiling scheme.</summary>
/// <param name="Zoom">Zoom level from 0 upward.</param>
/// <param name="X">Tile column from the west, within [0, 2^zoom).</param>
/// <param name="Y">Tile row from the north, within [0, 2^zoom).</param>
public readonly record struct MapTileId(int Zoom, int X, int Y)
{
    /// <summary>Compact canonical key, e.g. "12/2044/1362".</summary>
    public string Key => $"{Zoom.ToString(CultureInfo.InvariantCulture)}/{X.ToString(CultureInfo.InvariantCulture)}/{Y.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Immutable snapshot of the visible map area: geographic centre, clamped zoom level
/// (<see cref="MinZoomLevel"/>..<see cref="MaxZoomLevel"/>) and pixel size. Computes the visible
/// tile range including at most <see cref="LookAheadTiles"/> of look-ahead, and converts between
/// geographic points, tile origins and screen positions.
/// </summary>
public sealed class ViewportState
{
    /// <summary>Minimum supported zoom level.</summary>
    public const int MinZoomLevel = 2;

    /// <summary>Maximum supported zoom level.</summary>
    public const int MaxZoomLevel = 19;

    /// <summary>Maximum look-ahead beyond the visible range; modest by design.</summary>
    public const int LookAheadTiles = 1;

    /// <summary>Creates the viewport state; the zoom level is clamped to supported bounds.</summary>
    public ViewportState(GeoPoint centerGeoPoint, int zoomLevel, double widthPx, double heightPx)
    {
        CenterGeoPoint = centerGeoPoint;
        ZoomLevel = Math.Clamp(zoomLevel, MinZoomLevel, MaxZoomLevel);
        WidthPx = Math.Max(0d, widthPx);
        HeightPx = Math.Max(0d, heightPx);
        var (centerTileX, centerTileY) = WebMercatorProjection.LatLonToTileXY(ZoomLevel, centerGeoPoint.Latitude, centerGeoPoint.Longitude);
        _topLeftPixelX = centerTileX * MapTilePresenter.PixelsPerTile - WidthPx / 2d;
        _topLeftPixelY = centerTileY * MapTilePresenter.PixelsPerTile - HeightPx / 2d;
    }

    private readonly double _topLeftPixelX;
    private readonly double _topLeftPixelY;

    /// <summary>Geographic point currently at the centre of the viewport.</summary>
    public GeoPoint CenterGeoPoint { get; }

    /// <summary>Effective zoom level after clamping.</summary>
    public int ZoomLevel { get; }

    /// <summary>Viewport width in device-independent pixels.</summary>
    public double WidthPx { get; }

    /// <summary>Viewport height in device-independent pixels.</summary>
    public double HeightPx { get; }

    /// <summary>Returns every tile intersecting the viewport plus up to one look-ahead ring.</summary>
    public IReadOnlyList<MapTileId> VisibleTiles()
    {
        var tiles = new List<MapTileId>();
        var extent = 1 << ZoomLevel;
        var minX = Math.Clamp((int)Math.Floor(_topLeftPixelX / MapTilePresenter.PixelsPerTile) - LookAheadTiles, 0, extent - 1);
        var maxX = Math.Clamp((int)Math.Floor((_topLeftPixelX + WidthPx) / MapTilePresenter.PixelsPerTile) + LookAheadTiles, 0, extent - 1);
        var minY = Math.Clamp((int)Math.Floor(_topLeftPixelY / MapTilePresenter.PixelsPerTile) - LookAheadTiles, 0, extent - 1);
        var maxY = Math.Clamp((int)Math.Floor((_topLeftPixelY + HeightPx) / MapTilePresenter.PixelsPerTile) + LookAheadTiles, 0, extent - 1);
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
            tiles.Add(new MapTileId(ZoomLevel, x, y));
        return tiles;
    }

    /// <summary>Returns whether the tile lies inside the current visible range including look-ahead.</summary>
    public bool Contains(MapTileId tileId)
    {
        if (tileId.Zoom != ZoomLevel) return false;
        return VisibleTiles().Contains(tileId);
    }

    /// <summary>Top-left screen position (device-independent pixels) of a tile within the viewport.</summary>
    public (double X, double Y) TileOriginOnScreen(MapTileId tileId) =>
        (tileId.X * MapTilePresenter.PixelsPerTile - _topLeftPixelX, tileId.Y * MapTilePresenter.PixelsPerTile - _topLeftPixelY);

    /// <summary>Screen position (device-independent pixels) of a geographic point within the viewport.</summary>
    public (double X, double Y) GeoPointToScreenPx(GeoPoint point)
    {
        var (tileX, tileY) = WebMercatorProjection.LatLonToTileXY(ZoomLevel, point.Latitude, point.Longitude);
        return (tileX * MapTilePresenter.PixelsPerTile - _topLeftPixelX, tileY * MapTilePresenter.PixelsPerTile - _topLeftPixelY);
    }
}

/// <summary>
/// Capability-aware image resolver for raster tiles: decodes "havenmaptile://{z}/{x}/{y}" sources
/// from the local tile cache directory maintained by OsmRasterTileSource. Network, file and other
/// schemes are rejected so the scene can only ever show locally cached tiles.
/// </summary>
public sealed class MapTileImageResolver : IHavenAvaloniaImageResolver
{
    private const int MaximumCachedImages = 512;

    private readonly object _sync = new();
    private readonly Dictionary<string, IImage> _decoded = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recency = new();
    private readonly string _cacheDirectory;

    /// <summary>Creates the resolver over the tile source's disk cache directory.</summary>
    public MapTileImageResolver(string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory)) throw new ArgumentException("Cache directory is required.", nameof(cacheDirectory));
        _cacheDirectory = cacheDirectory;
    }

    /// <summary>Creates the resolver over a disk-caching tile source.</summary>
    public MapTileImageResolver(ITileSource tileSource)
    {
        ArgumentNullException.ThrowIfNull(tileSource);
        _cacheDirectory = tileSource as OsmRasterTileSource is { } diskSource
            ? diskSource.CacheDirectory
            : throw new ArgumentException("The tile source does not expose a local disk cache.", nameof(tileSource));
    }

    internal const string SourceSchemePrefix = "havenmaptile://";

    /// <inheritdoc />
    public bool TryResolve(string source, out IImage? image)
    {
        image = null;
        if (string.IsNullOrWhiteSpace(source) || !source.StartsWith(SourceSchemePrefix, StringComparison.OrdinalIgnoreCase)) return false;

        lock (_sync)
        {
            if (_decoded.TryGetValue(source, out var cached))
            {
                Touch(source);
                image = cached;
                return true;
            }
        }

        if (!TryGetCachePath(source, out var cachePath)) return false;
        try
        {
            using var stream = File.OpenRead(cachePath);
            var decoded = new Bitmap(stream);
            lock (_sync)
            {
                if (_decoded.TryGetValue(source, out var raceWinner))
                {
                    image = raceWinner;
                    return true;
                }
                _decoded[source] = decoded;
                Touch(source);
                while (_recency.Count > MaximumCachedImages)
                {
                    var eldest = _recency.First!.Value;
                    _recency.RemoveFirst();
                    _decoded.Remove(eldest);
                }
            }
            image = decoded;
            return true;
        }
        catch (Exception failure) when (failure is IOException or ArgumentException)
        {
            return false;
        }
    }

    private void Touch(string source)
    {
        _recency.Remove(source);
        _recency.AddLast(source);
    }

    private bool TryGetCachePath(string source, out string cachePath)
    {
        cachePath = string.Empty;
        var parts = source[SourceSchemePrefix.Length..].Split('/');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var zoom) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)) return false;
        if (zoom is < 0 or > ViewportState.MaxZoomLevel) return false;
        var extent = 1 << zoom;
        if (x < 0 || x >= extent || y < 0 || y >= extent) return false;
        cachePath = Path.Combine(_cacheDirectory,
            zoom.ToString(CultureInfo.InvariantCulture),
            x.ToString(CultureInfo.InvariantCulture),
            y.ToString(CultureInfo.InvariantCulture) + ".png");
        return true;
    }
}
