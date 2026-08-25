/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/Maps/OpenStreetMapService.cs, in the Infrastructure layer.
 * What: Owns OpenStreetMapService — the IMapService implementation over Nominatim search,
 *       geocoding and reverse geocoding — delegating routing to OsrmRoutingService.
 * How: Uses the shared named HttpClient ("Haven.Maps"), enforces the Nominatim rate limit
 *      client-side through NominatimRateLimiter, parses JSON with System.Text.Json, and returns
 *      empty/null results for recoverable network failures while letting cancellation propagate.
 * Why: External geocoding integration details must stay in Infrastructure; provider endpoints are
 *      overridable via configuration (environment variables) so providers can be switched without
 *      a software update.
 * Maintenance: Preserve the provider terms — ≥1100 ms between Nominatim calls, identifying
 *              User-Agent, HTTPS-only endpoints, attribution via MapsAttribution, and no bulk
 *              or background geocoding.
 */

using System.Globalization;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>IMapService implementation backed by Nominatim (search/geocoding) and OSRM (routing).</summary>
public sealed class OpenStreetMapService : IMapService
{
    /// <summary>Name of the shared HttpClient used by every Maps provider integration.</summary>
    public const string HttpClientName = "Haven.Maps";

    /// <summary>User-Agent sent to OpenStreetMap services so requests identify Haven per their usage policies.</summary>
    public const string UserAgent = "Haven/1.0 (+https://haven.app)";

    /// <summary>Default Nominatim endpoint; override with HAVEN_MAPS_NOMINATIM_BASE.</summary>
    public const string DefaultNominatimBaseAddress = "https://nominatim.openstreetmap.org";

    /// <summary>Environment variable overriding the Nominatim base address.</summary>
    public const string NominatimBaseOverride = "HAVEN_MAPS_NOMINATIM_BASE";

    private const string SearchPath = "/search";
    private const string ReversePath = "/reverse";
    private const int MaximumSearchLimit = 50;

    private readonly HttpClient _client;
    private readonly NominatimRateLimiter _rateLimiter = new();
    private readonly OsrmRoutingService _routing;

    /// <summary>Creates the service over the shared named HttpClient and an OSRM routing delegate.</summary>
    public OpenStreetMapService(IHttpClientFactory httpClientFactory, OsrmRoutingService routing)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));
        _client = httpClientFactory.CreateClient(HttpClientName);
    }

    /// <inheritdoc />
    public async Task<MapSearchResult> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var trimmedQuery = query?.Trim();
        if (string.IsNullOrEmpty(trimmedQuery)) return new MapSearchResult([]);
        var clampedLimit = Math.Clamp(limit, 1, MaximumSearchLimit);
        await _rateLimiter.DelayUntilTurnAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var requestUri = $"{ResolveBase(NominatimBaseOverride, DefaultNominatimBaseAddress)}{SearchPath}" +
                             $"?q={Uri.EscapeDataString(trimmedQuery)}&format=jsonv2&limit={clampedLimit.ToString(CultureInfo.InvariantCulture)}&addressdetails=0";
            using var document = await GetJsonDocumentAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return new MapSearchResult([]);
            var places = new List<MapPlace>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var hit = TryGetNominatimHit(element);
                if (hit is null) continue;
                places.Add(new MapPlace(
                    hit.Id,
                    hit.DisplayName,
                    hit.DetailLine,
                    new GeoPoint(hit.Latitude, hit.Longitude),
                    hit.Category));
            }
            return new MapSearchResult(places);
        }
        catch (Exception failure) when (failure is HttpRequestException or JsonException or UriFormatException or FormatException
            || (failure is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new MapSearchResult([]);
        }
    }

    /// <inheritdoc />
    public async Task<GeoPoint?> GeocodeAsync(string query, CancellationToken cancellationToken)
    {
        var trimmedQuery = query?.Trim();
        if (string.IsNullOrEmpty(trimmedQuery)) return null;
        var results = await SearchAsync(trimmedQuery, 1, cancellationToken).ConfigureAwait(false);
        return results.Places.Count > 0 ? results.Places[0].Location : null;
    }

    /// <inheritdoc />
    public async Task<string?> ReverseGeocodeAsync(GeoPoint point, CancellationToken cancellationToken)
    {
        await _rateLimiter.DelayUntilTurnAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var requestUri = $"{ResolveBase(NominatimBaseOverride, DefaultNominatimBaseAddress)}{ReversePath}" +
                             $"?lat={FormatCoordinate(point.Latitude)}&lon={FormatCoordinate(point.Longitude)}&format=jsonv2&zoom=18";
            using var document = await GetJsonDocumentAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (document.RootElement.TryGetProperty("error", out _)) return null;
            return document.RootElement.TryGetProperty("display_name", out var displayName) &&
                   displayName.ValueKind == JsonValueKind.String
                ? displayName.GetString()
                : null;
        }
        catch (Exception failure) when (failure is HttpRequestException or JsonException or UriFormatException
            || (failure is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task<MapRoute?> GetRouteAsync(GeoPoint start, GeoPoint end, MapTravelProfile profile, CancellationToken cancellationToken)
        => _routing.GetRouteAsync(start, end, profile, cancellationToken);

    private async Task<JsonDocument> GetJsonDocumentAsync(string requestUri, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(requestUri);
        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage CreateRequest(string requestUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        // The named client also sets this header at registration; setting it per request keeps the
        // provider terms honoured even if the shared registration changes.
        request.Headers.UserAgent.ParseAdd(UserAgent);
        return request;
    }

    private static string ResolveBase(string overrideVariable, string fallback)
    {
        var custom = Environment.GetEnvironmentVariable(overrideVariable)?.Trim().TrimEnd('/');
        return !string.IsNullOrWhiteSpace(custom) && Uri.TryCreate(custom, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? custom
            : fallback;
    }

    private static string FormatCoordinate(double degrees) =>
        degrees.ToString("0.#######", CultureInfo.InvariantCulture);

    private static NominatimPlace? TryGetNominatimHit(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!TryGetString(element, "display_name", out var displayName) || string.IsNullOrWhiteSpace(displayName)) return null;
        if (!TryParseDegrees(element, "lat", out var latitude) || !TryParseDegrees(element, "lon", out var longitude)) return null;

        string id;
        if (TryGetString(element, "osm_type", out var osmType) && element.TryGetProperty("osm_id", out var osmId))
            id = $"{osmType}-{osmId.GetRawText()}";
        else if (element.TryGetProperty("place_id", out var placeId))
            id = $"place-{placeId.GetRawText()}";
        else
            id = $"nominatim-{latitude.ToString(CultureInfo.InvariantCulture)}-{longitude.ToString(CultureInfo.InvariantCulture)}";

        TryGetString(element, "name", out var name);
        var primaryLabel = string.IsNullOrWhiteSpace(name) ? displayName.Split(',')[0].Trim() : name!.Trim();
        TryGetString(element, "addresstype", out var addressType);
        TryGetString(element, "type", out var type);
        var category = FirstNonEmpty(addressType, type);
        var detailLine = primaryLabel.Equals(displayName, StringComparison.Ordinal) ? null : displayName;
        return new NominatimPlace(id, primaryLabel, detailLine, latitude, longitude, category);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString();
        return true;
    }

    private static bool TryParseDegrees(JsonElement element, string propertyName, out double degrees)
    {
        degrees = 0d;
        if (!TryGetString(element, propertyName, out var raw) ||
            string.IsNullOrWhiteSpace(raw) ||
            !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out degrees)) return false;
        return double.IsFinite(degrees);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private sealed record NominatimPlace(
        string Id,
        string DisplayName,
        string? DetailLine,
        double Latitude,
        double Longitude,
        string? Category);
}
