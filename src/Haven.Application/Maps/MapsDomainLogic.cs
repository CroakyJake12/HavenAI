/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Maps/MapsDomainLogic.cs, in the Application layer.
 * What: Owns the pure, platform-free Maps rules shared across layers — WebMercatorProjection,
 *       NominatimRateLimiter and MapRouteSummaries.
 * How: Static/immutable helpers with no I/O; NominatimRateLimiter takes a TimeProvider so tests
 *      can inject a fake clock instead of waiting real time.
 * Why: These rules are consumed by Infrastructure providers and the Desktop presenter but must be
 *      unit-testable from the shared test suite, which references only Core and Application.
 * Maintenance: Provider terms encoded here (≥1100 ms between geocoder calls, 256 px tiles,
 *              Web-Mercator maths) are contractual — change them only with the provider terms.
 */

using System.Globalization;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Web-Mercator (EPSG:3857) tile mathematics at the industry-standard 256 px tile size.
/// Tile coordinates follow the slippy-map convention: the origin is top-left, x grows east,
/// y grows south, and both span [0, 2^zoom].
/// </summary>
public static class WebMercatorProjection
{
    /// <summary>Edge length of one raster map tile in device-independent pixels.</summary>
    public const double PixelsPerTile = 256d;

    /// <summary>Northern/Southern limit of Web-Mercator coverage in degrees of latitude.</summary>
    public const double MaxLatitudeDegrees = 85.05112878d;

    /// <summary>Converts decimal degrees to fractional tile coordinates at the given zoom.</summary>
    public static (double TileX, double TileY) LatLonToTileXY(int zoomLevel, double latitudeDegrees, double longitudeDegrees)
    {
        var tilesAtZoom = Math.Pow(2d, zoomLevel);
        var latitudeRadians = Math.Clamp(latitudeDegrees, -MaxLatitudeDegrees, MaxLatitudeDegrees) * Math.PI / 180d;
        var tileX = (longitudeDegrees + 180d) / 360d * tilesAtZoom;
        var tileY = (1d - Math.Log(Math.Tan(latitudeRadians) + 1d / Math.Cos(latitudeRadians)) / Math.PI) / 2d * tilesAtZoom;
        return (tileX, tileY);
    }

    /// <summary>Converts fractional tile coordinates back to decimal degrees.</summary>
    public static GeoPoint TileXYToLatLon(int zoomLevel, double tileX, double tileY)
    {
        var tilesAtZoom = Math.Pow(2d, zoomLevel);
        var longitude = tileX / tilesAtZoom * 360d - 180d;
        var mercatorN = Math.PI - 2d * Math.PI * tileY / tilesAtZoom;
        var latitude = 180d / Math.PI * Math.Atan(0.5d * (Math.Exp(mercatorN) - Math.Exp(-mercatorN)));
        return new GeoPoint(latitude, longitude);
    }
}

/// <summary>
/// Client-side rate limiter enforcing the geocoder's terms of use: at least
/// <see cref="MinimumIntervalMilliseconds"/> must pass between consecutive requests. The interval
/// is reserved when a turn is requested (not when it completes) so queued callers cannot
/// queue-jump after a delay.
/// </summary>
public sealed class NominatimRateLimiter(TimeProvider? timeProvider = null)
{
    /// <summary>Minimum spacing between geocoder requests in milliseconds.</summary>
    public const int MinimumIntervalMilliseconds = 1100;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private DateTimeOffset _lastRequestScheduledAt;

    /// <summary>Returns how long a request issued at <paramref name="requestedAtUtc"/> must still wait.</summary>
    public TimeSpan RemainingWait(DateTimeOffset requestedAtUtc)
    {
        var elapsedSinceLast = requestedAtUtc - _lastRequestScheduledAt;
        var minimumInterval = TimeSpan.FromMilliseconds(MinimumIntervalMilliseconds);
        return _lastRequestScheduledAt == default || elapsedSinceLast >= minimumInterval
            ? TimeSpan.Zero
            : minimumInterval - elapsedSinceLast;
    }

    /// <summary>Records that the request slot starting at <paramref name="scheduledAtUtc"/> has been taken.</summary>
    public void MarkRequested(DateTimeOffset scheduledAtUtc)
    {
        if (scheduledAtUtc > _lastRequestScheduledAt) _lastRequestScheduledAt = scheduledAtUtc;
    }

    /// <summary>Waits until this caller's turn, reserving the slot immediately so later callers queue behind it.</summary>
    public async Task DelayUntilTurnAsync(CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow();
        var remaining = RemainingWait(nowUtc);
        MarkRequested(nowUtc + remaining);
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Human-readable formatting rules for routes and travel profiles.</summary>
public static class MapRouteSummaries
{
    /// <summary>Formats distance and duration as a compact summary such as "12.4 km · 18 min".</summary>
    public static string Format(double distanceMeters, TimeSpan duration)
    {
        var distanceText = distanceMeters >= 1000d
            ? (distanceMeters / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + " km"
            : Math.Max(0d, Math.Round(distanceMeters)).ToString("0", CultureInfo.InvariantCulture) + " m";
        return $"{distanceText} · {FormatDuration(duration)}";
    }

    /// <summary>Formats a duration as minutes below an hour, otherwise hours plus minutes.</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        var totalMinutes = Math.Max(0, (int)Math.Round(duration.TotalMinutes));
        if (totalMinutes < 1) return "<1 min";
        if (totalMinutes < 60) return $"{totalMinutes} min";
        return $"{totalMinutes / 60} h {totalMinutes % 60:00} min";
    }

    /// <summary>Maps a travel profile onto the routing provider's lowercase profile path segment.</summary>
    public static string ToProfileName(MapTravelProfile profile) => profile switch
    {
        MapTravelProfile.Walking => "walking",
        MapTravelProfile.Cycling => "cycling",
        _ => "driving"
    };
}
