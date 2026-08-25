/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/Maps/MapsModels.cs, in the dependency-free Core layer, where shared domain models and rules live.
 * What: Owns the stable Maps value types — GeoPoint, MapPlace, MapRoute, SavedMapPlace, MapTravelProfile,
 *       MapSearchResult and MapsAttribution — shared by Application contracts, Infrastructure providers,
 *       the Desktop map surface and the Actions system.
 * How: Immutable records carry provider-neutral shapes; MapsAttribution holds the mandatory
 *      OpenStreetMap attribution that every map surface must keep visible.
 * Why: Maps data crosses layer boundaries, so the shapes must stay free of UI, storage and network details.
 * Maintenance: Treat persisted values (SavedMapPlace.Id) as stable identifiers; never renumber MapTravelProfile.
 */

namespace Haven.Core;

/// <summary>A geographic coordinate in decimal degrees (WGS 84).</summary>
/// <param name="Latitude">Latitude in decimal degrees, clamped to Web-Mercator coverage by consumers.</param>
/// <param name="Longitude">Longitude in decimal degrees; values outside ±180 wrap to the equivalent meridian.</param>
public sealed record GeoPoint(double Latitude, double Longitude);

/// <summary>A place returned by search or geocoding, or otherwise surfaced on a map.</summary>
/// <param name="Id">Stable identifier for the place within its originating result set.</param>
/// <param name="DisplayName">Human-facing primary label.</param>
/// <param name="DetailLine">Optional secondary context such as street, city or category detail.</param>
/// <param name="Location">The geographic position of the place.</param>
/// <param name="Category">Optional place category, e.g. "city" or "restaurant".</param>
public sealed record MapPlace(
    string Id,
    string DisplayName,
    string? DetailLine,
    GeoPoint Location,
    string? Category);

/// <summary>A computed journey between two points.</summary>
/// <param name="Points">Ordered polyline vertices from start to destination.</param>
/// <param name="DistanceMeters">Total travel distance in metres.</param>
/// <param name="Duration">Estimated uninterrupted travel time.</param>
/// <param name="Profile">The travel profile used, matching the routing provider's naming.</param>
/// <param name="SummaryText">Compact human-readable summary such as "12.4 km · 18 min".</param>
public sealed record MapRoute(
    GeoPoint[] Points,
    double DistanceMeters,
    TimeSpan Duration,
    string Profile,
    string SummaryText);

/// <summary>A user-saved place persisted locally by Haven.</summary>
/// <param name="Id">Stable saved-place identifier; existing values must never be recycled.</param>
/// <param name="DisplayName">Human-facing primary label.</param>
/// <param name="Note">Optional free-text note supplied by the user.</param>
/// <param name="Location">The geographic position of the saved place.</param>
/// <param name="SavedAt">When the place was saved (UTC offset included).</param>
public sealed record SavedMapPlace(
    string Id,
    string DisplayName,
    string? Note,
    GeoPoint Location,
    DateTimeOffset SavedAt);

/// <summary>Travel profiles offered by the routing provider. Persisted ordinal; never renumber.</summary>
public enum MapTravelProfile
{
    Driving = 0,
    Walking = 1,
    Cycling = 2
}

/// <summary>The result of a place search. Empty when nothing matched or the lookup failed.</summary>
/// <param name="Places">Matching places, most relevant first.</param>
public sealed record MapSearchResult(IReadOnlyList<MapPlace> Places);

/// <summary>Mandatory OpenStreetMap attribution shown on every Haven map surface.</summary>
public static class MapsAttribution
{
    /// <summary>Attribution text that must remain visible on any rendered map.</summary>
    public const string Text = "© OpenStreetMap contributors";

    /// <summary>Link target describing the OpenStreetMap licence and contributors.</summary>
    public const string LicenceUrl = "https://www.openstreetmap.org/copyright";
}
