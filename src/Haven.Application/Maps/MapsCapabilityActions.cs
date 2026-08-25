/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Maps/MapsCapabilityActions.cs, in the Application layer.
 * What: Owns the stable capability action descriptors for the Maps feature — SearchPlaces,
 *       ShowOnMap, GetDirections and SavePlace — consumed later by the Actions system.
 * How: Simple POCO records with stable keys ("maps.search-places", …) and user-facing names and
 *      descriptions; the catalog is immutable and enumerable for registration surfaces.
 * Why: Actions must bind to declared capabilities with stable keys so registrations survive
 *      provider switches and UI changes without renumbering.
 * Maintenance: Keys are stable identifiers — never rename or recycle one; add new actions rather
 *              than replacing existing entries.
 */

namespace Haven.Application;

/// <summary>A user-invokable Maps capability exposed to the Actions system.</summary>
/// <param name="Key">Stable capability key, e.g. "maps.search-places".</param>
/// <param name="Name">User-facing action name.</param>
/// <param name="Description">User-facing explanation of what the action does.</param>
public sealed record MapsCapabilityAction(string Key, string Name, string Description);

/// <summary>Catalog of Maps capability actions with stable keys.</summary>
public static class MapsCapabilityActions
{
    /// <summary>Search for places by free-text query.</summary>
    public static readonly MapsCapabilityAction SearchPlaces = new(
        "maps.search-places",
        "Search places",
        "Find places by name using OpenStreetMap data and show the results in Maps.");

    /// <summary>Show a known place on the map.</summary>
    public static readonly MapsCapabilityAction ShowOnMap = new(
        "maps.show-on-map",
        "Show on map",
        "Centre the map on a chosen place and drop a marker at its location.");

    /// <summary>Compute directions between two marked places.</summary>
    public static readonly MapsCapabilityAction GetDirections = new(
        "maps.get-directions",
        "Get directions",
        "Route between two marked places using the selected driving, walking or cycling profile.");

    /// <summary>Save a marked place locally.</summary>
    public static readonly MapsCapabilityAction SavePlace = new(
        "maps.save-place",
        "Save place",
        "Keep a marked place in Haven's local saved places list.");

    /// <summary>All Maps capability actions in catalog order.</summary>
    public static readonly IReadOnlyList<MapsCapabilityAction> All =
        [SearchPlaces, ShowOnMap, GetDirections, SavePlace];
}
