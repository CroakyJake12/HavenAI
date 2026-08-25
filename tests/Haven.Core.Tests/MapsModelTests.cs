/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/MapsModelTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns the pure Maps rule tests — Web-Mercator round trips, the Nominatim rate
 *       limiter's clock-injected spacing, route summary formatting and MapsStoreLogic normalisation.
 * How: Tests exercise only platform-free Application/Core rules with a fake TimeProvider, keeping
 *      failures close to user-visible or contractual behavior.
 * Why: Provider terms (≥1100 ms spacing), projection maths, summary formatting and store caps are
 *      contracts that must not drift between Infrastructure implementations and the UI.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class WebMercatorProjectionTests
{
    public static TheoryData<double, double> SamplePoints => new()
    {
        { 0d, 0d },
        { 51.5074d, -0.1278d },
        { 48.8566d, 2.3522d },
        { -33.8688d, 151.2093d },
        { 35.6895d, 139.6917d },
        { -54.8d, -68.3d }
    };

    [Theory]
    [MemberData(nameof(SamplePoints))]
    public void TileCoordinateRoundTripPreservesLocation(double latitude, double longitude)
    {
        foreach (var zoom in new[] { 2, 7, 13, 19 })
        {
            var (tileX, tileY) = WebMercatorProjection.LatLonToTileXY(zoom, latitude, longitude);
            var restored = WebMercatorProjection.TileXYToLatLon(zoom, tileX, tileY);

            Assert.True(Math.Abs(restored.Latitude - latitude) < 1e-5);
            Assert.True(Math.Abs(restored.Longitude - longitude) < 1e-5);
        }
    }

    [Fact]
    public void OriginMapsToCentreOfSingleTileWorld()
    {
        var (tileX, tileY) = WebMercatorProjection.LatLonToTileXY(0, 0d, 0d);

        Assert.Equal(0.5d, tileX, 12);
        Assert.Equal(0.5d, tileY, 12);
    }

    [Fact]
    public void TilesUseTheStandardTwoHundredFiftySixPixelSize()
    {
        Assert.Equal(256d, WebMercatorProjection.PixelsPerTile);
    }

    [Fact]
    public void LatitudeBeyondMercatorCoverageIsClampedNotWrapped()
    {
        var extreme = WebMercatorProjection.LatLonToTileXY(4, 89.9d, 0d);
        var atLimit = WebMercatorProjection.LatLonToTileXY(4, WebMercatorProjection.MaxLatitudeDegrees, 0d);

        Assert.True(WebMercatorProjection.MaxLatitudeDegrees < 85.06d);
        Assert.Equal(atLimit.TileY, extreme.TileY, 9);
        Assert.Equal(atLimit.TileX, extreme.TileX, 9);
    }

    [Fact]
    public void TileCoordinatesStayWithinSlippyMapBounds()
    {
        const int zoom = 5;
        var (tileX, tileY) = WebMercatorProjection.LatLonToTileXY(zoom, 40.7128d, -74.006d);

        Assert.InRange(tileX, 0d, Math.Pow(2, zoom));
        Assert.InRange(tileY, 0d, Math.Pow(2, zoom));
    }
}

public sealed class NominatimRateLimiterTests
{
    private sealed class FakeTimeProvider(DateTimeOffset startUtc) : TimeProvider
    {
        private DateTimeOffset _nowUtc = startUtc;

        public void AdvanceBy(TimeSpan delta) => _nowUtc += delta;

        public override DateTimeOffset GetUtcNow() => _nowUtc;
    }

    private static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void FirstRequestNeedsNoWait()
    {
        var clock = Clock();
        var limiter = new NominatimRateLimiter(clock);

        Assert.Equal(TimeSpan.Zero, limiter.RemainingWait(clock.GetUtcNow()));
    }

    [Fact]
    public void RapidSecondCallMustWaitOutTheMinimumInterval()
    {
        var clock = Clock();
        var limiter = new NominatimRateLimiter(clock);

        Assert.Equal(TimeSpan.Zero, limiter.RemainingWait(clock.GetUtcNow()));
        limiter.MarkRequested(clock.GetUtcNow());

        clock.AdvanceBy(TimeSpan.FromMilliseconds(100));
        var remaining = limiter.RemainingWait(clock.GetUtcNow());

        Assert.True(remaining > TimeSpan.Zero);
        Assert.Equal(NominatimRateLimiter.MinimumIntervalMilliseconds - 100, remaining.TotalMilliseconds, 0);

        clock.AdvanceBy(remaining);
        Assert.Equal(TimeSpan.Zero, limiter.RemainingWait(clock.GetUtcNow()));
    }

    [Fact]
    public async Task FirstTurnCompletesWithoutRealWaiting()
    {
        var clock = Clock();
        var limiter = new NominatimRateLimiter(clock);

        await limiter.DelayUntilTurnAsync(CancellationToken.None);

        // Reaching here proves the async turn path ran and reserved exactly one slot.
        Assert.Equal(
            (double)NominatimRateLimiter.MinimumIntervalMilliseconds,
            limiter.RemainingWait(clock.GetUtcNow()).TotalMilliseconds,
            0);
    }

    [Fact]
    public void ReservedSlotsScheduleSequentialTurnsFullIntervalsApart()
    {
        var clock = Clock();
        var limiter = new NominatimRateLimiter(clock);

        limiter.MarkRequested(clock.GetUtcNow());

        var firstWait = limiter.RemainingWait(clock.GetUtcNow());
        Assert.Equal((double)NominatimRateLimiter.MinimumIntervalMilliseconds, firstWait.TotalMilliseconds, 0);

        // A queued caller reserves its slot at schedule time, one full interval later.
        limiter.MarkRequested(clock.GetUtcNow() + firstWait);

        Assert.Equal(
            (double)(2 * NominatimRateLimiter.MinimumIntervalMilliseconds),
            limiter.RemainingWait(clock.GetUtcNow()).TotalMilliseconds,
            0);
    }
}

public sealed class MapRouteSummaryTests
{
    [Fact]
    public void KilometresAndMinutesFormatCompactSummary()
    {
        var summary = MapRouteSummaries.Format(12400d, TimeSpan.FromMinutes(18));

        Assert.Equal("12.4 km · 18 min", summary);
    }

    [Fact]
    public void ShortDistancesUseMetresAndSubMinuteRounding()
    {
        Assert.Equal("850 m · <1 min", MapRouteSummaries.Format(850d, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void LongDurationsSplitIntoHoursAndMinutes()
    {
        Assert.Equal("2.5 km · 1 h 15 min", MapRouteSummaries.Format(2500d, TimeSpan.FromMinutes(75)));
    }

    [Fact]
    public void ProfilesMapToLowercaseRoutingPathSegments()
    {
        Assert.Equal("driving", MapRouteSummaries.ToProfileName(MapTravelProfile.Driving));
        Assert.Equal("walking", MapRouteSummaries.ToProfileName(MapTravelProfile.Walking));
        Assert.Equal("cycling", MapRouteSummaries.ToProfileName(MapTravelProfile.Cycling));
    }
}

public sealed class MapsStoreLogicTests
{
    private static SavedMapPlace Place(string id, string name, string? note, double latitude, double longitude, int minutesAgo) =>
        new(id, name, note, new GeoPoint(latitude, longitude), DateTimeOffset.Now.AddMinutes(-minutesAgo));

    [Fact]
    public void SavedPlacesAreDeduplicatedByIdKeepingTheNewestEntry()
    {
        var older = Place("a", "Old label", "old note", 10d, 20d, minutesAgo: 30);
        var newer = Place("a", "New label", null, 11d, 21d, minutesAgo: 5);
        var newestOther = Place("b", "Other", null, 0d, 0d, minutesAgo: 1);

        var normalised = MapsStoreLogic.NormaliseSavedPlaces([older, newer, newestOther]);

        Assert.Equal(2, normalised.Count);
        Assert.Equal("Other", normalised[0].DisplayName);
        var merged = Assert.Single(normalised, place => place.Id == "a");
        Assert.Equal("New label", merged.DisplayName);
        Assert.Equal(11d, merged.Location.Latitude);
        Assert.Null(merged.Note);
    }

    [Fact]
    public void SavedPlacesOrderNewestFirstAndCapAtTwoHundred()
    {
        var total = MapsStoreLogic.MaxSavedPlaces + 25;
        var places = new List<SavedMapPlace?>();
        for (var index = 0; index < total; index++)
            places.Add(Place($"id-{index}", $"Place {index}", null, 0d, 0d, minutesAgo: total - index));

        var normalised = MapsStoreLogic.NormaliseSavedPlaces(places);

        Assert.Equal(MapsStoreLogic.MaxSavedPlaces, normalised.Count);
        Assert.Equal($"Place {total - 1}", normalised[0].DisplayName);
        Assert.Equal($"Place {total - MapsStoreLogic.MaxSavedPlaces}", normalised[^1].DisplayName);
    }

    [Fact]
    public void SavedPlaceFieldsAreTrimmedCoordinatesNormalisedAndBlanksCleared()
    {
        var normalised = MapsStoreLogic.NormaliseSavedPlaces([
            Place("  spaced  ", "  Padding Station  ", "   ", 95d, 200d, minutesAgo: 0),
            Place("", "No id", null, 0d, 0d, minutesAgo: 0)
        ]);

        var entry = Assert.Single(normalised);
        Assert.Equal("spaced", entry.Id);
        Assert.Equal("Padding Station", entry.DisplayName);
        Assert.Null(entry.Note);
        Assert.Equal(90d, entry.Location.Latitude);
        Assert.Equal(-160d, entry.Location.Longitude);
    }

    [Fact]
    public void RecentSearchesDeduplicateCaseInsensitivelyKeepingTheMostRecentFirst()
    {
        var normalised = MapsStoreLogic.NormaliseRecentSearches([
            "  berlin ",
            "Vienna",
            "BERLIN",
            null,
            "",
            "vienna  "
        ]);

        Assert.Equal(["vienna", "BERLIN"], normalised.ToArray());
    }

    [Fact]
    public void RecentSearchesCapAtTwentyEntries()
    {
        var searches = Enumerable.Range(0, MapsStoreLogic.MaxRecentSearches + 15)
            .Select(index => $"query-{index}")
            .ToArray();

        var normalised = MapsStoreLogic.NormaliseRecentSearches(searches);

        Assert.Equal(MapsStoreLogic.MaxRecentSearches, normalised.Count);
        Assert.Equal($"query-{MapsStoreLogic.MaxRecentSearches + 14}", normalised[0]);
        Assert.Equal("query-15", normalised[^1]);
    }

    [Fact]
    public void NullAndEmptyInputsProduceEmptyResults()
    {
        Assert.Empty(MapsStoreLogic.NormaliseSavedPlaces(null));
        Assert.Empty(MapsStoreLogic.NormaliseRecentSearches(null));
        Assert.Empty(MapsStoreLogic.NormaliseSavedPlaces([null]));
        Assert.Empty(MapsStoreLogic.NormaliseRecentSearches(["   ", null]));
    }
}
