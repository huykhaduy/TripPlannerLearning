using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Tests.TestDoubles;
using TripPlanner.Infrastructure.ExternalApis;
using Xunit;

namespace TripPlanner.Application.Tests.Infrastructure;

/// <summary>
/// Covers the three things this adapter is responsible for: building the URL
/// Geoapify actually requires, mapping its GeoJSON into our DTOs, and classifying
/// failures. The last one matters beyond this class — DestinationService decides
/// whether to cache a result based on "the provider failed" versus "the provider
/// found nothing", so an adapter that swallowed would make it cache an empty list
/// for 24 hours over a one-second network blip.
/// </summary>
public class GeoapifyClientTests
{
    private const string ApiKey = "test-key";

    private static GeoapifyClient CreateSut(
        StubHttpMessageHandler handler,
        RecordingLogger<GeoapifyClient>? logger = null) =>
        new(handler.CreateClient(),
            Options.Create(new GeoapifySettings { ApiKey = ApiKey }),
            logger ?? new RecordingLogger<GeoapifyClient>());

    // ------------------------------------------------------------------
    // SearchLocationsAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task SearchLocations_AsksTheAutocompleteEndpointWithTheEscapedQuery()
    {
        var handler = StubHttpMessageHandler.Returning("""{"features":[]}""");

        await CreateSut(handler).SearchLocationsAsync("Ha Noi");

        // AbsoluteUri, not ToString() — the latter hands back the *unescaped* form,
        // which would make this pass even if the query were never escaped.
        var url = handler.LastRequestUri.AbsoluteUri;
        Assert.Contains("v1/geocode/autocomplete", url);
        Assert.Contains("text=Ha%20Noi", url);
        Assert.Contains($"apiKey={ApiKey}", url);
    }

    [Fact]
    public async Task SearchLocations_DoesNotRestrictResultsByType()
    {
        // F1/US2 needs countries to surface alongside cities. A `type=city` filter
        // would quietly drop every country, and the filtering below would still
        // look correct — hence pinning the absence.
        var handler = StubHttpMessageHandler.Returning("""{"features":[]}""");

        await CreateSut(handler).SearchLocationsAsync("viet");

        Assert.DoesNotContain("type=", handler.LastRequestUri.Query);
    }

    [Fact]
    public async Task SearchLocations_OverFetchesBecauseTheResultTypeFilterDiscardsRows()
    {
        // The service caps at 5, but streets/districts get filtered out here first,
        // so asking for only 5 would often leave fewer than 5 usable suggestions.
        var handler = StubHttpMessageHandler.Returning("""{"features":[]}""");

        await CreateSut(handler).SearchLocationsAsync("ha");

        Assert.Contains("limit=10", handler.LastRequestUri.Query);
    }

    [Fact]
    public async Task SearchLocations_KeepsOnlyCitiesAndCountries()
    {
        var handler = StubHttpMessageHandler.Returning("""
            {"features":[
              {"properties":{"result_type":"city","city":"Hanoi","country":"Vietnam","lat":21.03,"lon":105.85}},
              {"properties":{"result_type":"street","city":"Hanoi","country":"Vietnam","lat":21.0,"lon":105.8}},
              {"properties":{"result_type":"country","country":"Vietnam","lat":16.0,"lon":106.0}}
            ]}
            """);

        var results = await CreateSut(handler).SearchLocationsAsync("viet");

        Assert.Equal(2, results.Count);
        Assert.Equal("Hanoi", results[0].Name);
        Assert.Equal(21.03, results[0].Latitude);
        Assert.Equal(105.85, results[0].Longitude);
        Assert.Equal("Vietnam", results[1].Name);
    }

    [Fact]
    public async Task SearchLocations_FallsBackThroughCityThenCountryThenFormatted()
    {
        var handler = StubHttpMessageHandler.Returning("""
            {"features":[
              {"properties":{"result_type":"country","formatted":"Somewhere, Nowhere","lat":1,"lon":2}}
            ]}
            """);

        var results = await CreateSut(handler).SearchLocationsAsync("x");

        Assert.Equal("Somewhere, Nowhere", Assert.Single(results).Name);
    }

    [Fact]
    public async Task SearchLocations_WhenTheBodyHasNoFeatures_ReturnsEmptyRatherThanNull()
    {
        var handler = StubHttpMessageHandler.Returning("""{}""");

        var results = await CreateSut(handler).SearchLocationsAsync("x");

        Assert.Empty(results);
    }

    // ------------------------------------------------------------------
    // GetAttractionsAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAttractions_PutsLongitudeBeforeLatitudeAndConvertsKmToMetres()
    {
        // Both are Geoapify quirks and both fail silently if reversed: swapped
        // coordinates return places from a different continent, and passing km as
        // metres searches a 5-metre circle that is always empty.
        var handler = StubHttpMessageHandler.Returning("""{"features":[]}""");

        await CreateSut(handler).GetAttractionsAsync(latitude: 21.03, longitude: 105.85, radiusKm: 5);

        Assert.Contains("filter=circle:105.85,21.03,5000", Uri.UnescapeDataString(handler.LastRequestUri.Query));
    }

    [Fact]
    public async Task GetAttractions_FormatsCoordinatesInvariantly()
    {
        // A server running under a comma-decimal locale would otherwise send
        // "circle:105,85,21,03,5000" and Geoapify would reject or misread it.
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("vi-VN");
        try
        {
            var handler = StubHttpMessageHandler.Returning("""{"features":[]}""");

            await CreateSut(handler).GetAttractionsAsync(21.03, 105.85, 5);

            Assert.Contains("105.85,21.03", Uri.UnescapeDataString(handler.LastRequestUri.Query));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task GetAttractions_LeavesCateringOutOfTheRequestedCategories()
    {
        // Restaurants would crowd actual sights out of the 20-result cap.
        var handler = StubHttpMessageHandler.Returning("""{"features":[]}""");

        await CreateSut(handler).GetAttractionsAsync(21.03, 105.85, 5);

        var query = Uri.UnescapeDataString(handler.LastRequestUri.Query);
        Assert.Contains("tourism.sights", query);
        Assert.DoesNotContain("catering", query);
    }

    [Fact]
    public async Task GetAttractions_MapsAPlaceIncludingItsMostSpecificCategory()
    {
        var handler = StubHttpMessageHandler.Returning("""
            {"features":[
              {"properties":{
                 "place_id":"geo-1","name":"Imperial Citadel",
                 "categories":["tourism","tourism.sights","tourism.sights.castle"],
                 "wiki_and_media":{"image":"https://img.test/citadel.jpg"}}}
            ]}
            """);

        var result = Assert.Single(await CreateSut(handler).GetAttractionsAsync(21, 105, 5));

        Assert.Equal("geo-1", result.ProviderId);
        Assert.Equal("Imperial Citadel", result.Name);
        // The deepest tag's last segment is the human-readable one.
        Assert.Equal("castle", result.Category);
        Assert.Equal("https://img.test/citadel.jpg", result.ImageUrl);
        Assert.Null(result.Rating); // Geoapify has no ratings at all
    }

    [Fact]
    public async Task GetAttractions_SkipsPlacesWithNoIdOrNoName()
    {
        // An id-less place cannot be re-fetched or added to a trip, and an unnamed
        // one renders as a bare street address.
        var handler = StubHttpMessageHandler.Returning("""
            {"features":[
              {"properties":{"name":"No id here"}},
              {"properties":{"place_id":"geo-2"}},
              {"properties":{"place_id":"geo-3","name":"Keeper"}}
            ]}
            """);

        var results = await CreateSut(handler).GetAttractionsAsync(21, 105, 5);

        Assert.Equal("geo-3", Assert.Single(results).ProviderId);
    }

    [Fact]
    public async Task GetAttractions_WithNoCategories_ReportsNoCategory()
    {
        var handler = StubHttpMessageHandler.Returning("""
            {"features":[{"properties":{"place_id":"geo-1","name":"Somewhere","categories":[]}}]}
            """);

        var result = Assert.Single(await CreateSut(handler).GetAttractionsAsync(21, 105, 5));

        Assert.Null(result.Category);
    }

    // ------------------------------------------------------------------
    // GetDestinationDetailsAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetDetails_MapsTheFullPlace()
    {
        var handler = StubHttpMessageHandler.Returning("""
            {"features":[
              {"properties":{
                 "place_id":"canonical-id","name":"Imperial Citadel",
                 "categories":["tourism.sights.castle"],
                 "description":"A citadel.","website":"https://citadel.test",
                 "opening_hours":"Mo-Su 08:00-17:00","formatted":"Hanoi, Vietnam",
                 "lat":21.03,"lon":105.84,
                 "wiki_and_media":{"image":"https://img.test/c.jpg"}}}
            ]}
            """);

        var result = await CreateSut(handler).GetDestinationDetailsAsync("requested-id");

        Assert.NotNull(result);
        Assert.Equal("Imperial Citadel", result.Name);
        Assert.Equal("castle", result.Category);
        Assert.Equal("A citadel.", result.Description);
        Assert.Equal("https://citadel.test", result.Website);
        Assert.Equal("Mo-Su 08:00-17:00", result.OpeningHours);
        Assert.Equal("Hanoi, Vietnam", result.Address);
        Assert.Equal(21.03, result.Latitude);
    }

    [Fact]
    public async Task GetDetails_EchoesTheRequestedIdNotTheCanonicalOne()
    {
        // ProviderId is the Destination cache key. Returning Geoapify's canonical id
        // instead would make a repeat "add to trip" miss the existing row and insert
        // a duplicate destination under a second id.
        var handler = StubHttpMessageHandler.Returning("""
            {"features":[{"properties":{"place_id":"canonical-id","name":"Somewhere"}}]}
            """);

        var result = await CreateSut(handler).GetDestinationDetailsAsync("requested-id");

        Assert.Equal("requested-id", result!.ProviderId);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task GetDetails_TreatsAnUnrecognisedIdAsNoSuchPlace(HttpStatusCode status)
    {
        // Geoapify answers 400 as well as 404 for ids it does not know; both mean
        // "no such destination", which the contract expresses as null rather than
        // as an exception the service would have to catch.
        var handler = StubHttpMessageHandler.ReturningStatus(status);

        var result = await CreateSut(handler).GetDestinationDetailsAsync("unknown-id");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDetails_TreatsAnEmptyFeatureListAsNoSuchPlace()
    {
        var handler = StubHttpMessageHandler.Returning("""{"features":[]}""");

        Assert.Null(await CreateSut(handler).GetDestinationDetailsAsync("unknown-id"));
    }

    [Fact]
    public async Task GetDetails_TreatsAnUnauthorizedKeyAsARealFault()
    {
        // 401 means our key is wrong — a configuration fault that must surface,
        // not a missing place. Folding it into the null branch would present a
        // broken deployment as "we could not find that destination".
        var handler = StubHttpMessageHandler.ReturningStatus(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => CreateSut(handler).GetDestinationDetailsAsync("geo-1"));
    }

    [Fact]
    public async Task GetDetails_EscapesTheProviderIdIntoTheQuery()
    {
        var handler = StubHttpMessageHandler.Returning("""{"features":[]}""");

        await CreateSut(handler).GetDestinationDetailsAsync("51a&b=c");

        Assert.Contains("id=51a%26b%3Dc", handler.LastRequestUri.Query);
    }

    // ------------------------------------------------------------------
    // Lenient parsing
    // ------------------------------------------------------------------

    [Fact]
    public async Task ANameThatArrivesAsANumberDoesNotBreakTheWholeResponse()
    {
        // OpenStreetMap names are free text but occasionally look numeric (a war
        // memorial named "1918"), and Geoapify then serializes them as a bare JSON
        // number. Without LenientStringConverter that one place kills the entire
        // response, so an unrelated search returns nothing.
        var handler = StubHttpMessageHandler.Returning("""
            {"features":[
              {"properties":{"place_id":"geo-1","name":1918}},
              {"properties":{"place_id":"geo-2","name":"Normal Place"}}
            ]}
            """);

        var results = await CreateSut(handler).GetAttractionsAsync(21, 105, 5);

        Assert.Equal(2, results.Count);
        Assert.Equal("1918", results[0].Name);
        Assert.Equal("Normal Place", results[1].Name);
    }

    // ------------------------------------------------------------------
    // Failure handling — log at the source, then rethrow
    // ------------------------------------------------------------------

    [Fact]
    public async Task ANetworkFailureIsLoggedAndRethrown()
    {
        // AddInfrastructure calls RemoveAllLoggers() on this client (the api key
        // rides in the query string and the default handlers log the full URI), so
        // without this log a Geoapify outage would be completely invisible.
        var logger = new RecordingLogger<GeoapifyClient>();
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => CreateSut(handler, logger).SearchLocationsAsync("hanoi"));

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("location search", warning.Message);
    }

    [Fact]
    public async Task ATimeoutIsLoggedAndRethrown()
    {
        // HttpClient surfaces its own timeout as TaskCanceledException, not
        // HttpRequestException — classifying only the latter would let a timeout
        // escape unlogged and uncaught.
        var logger = new RecordingLogger<GeoapifyClient>();
        var handler = StubHttpMessageHandler.Throwing(new TaskCanceledException("timed out"));

        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => CreateSut(handler, logger).GetAttractionsAsync(21, 105, 5));

        Assert.Contains("attractions lookup", Assert.Single(logger.Warnings).Message);
    }

    [Fact]
    public async Task AnUnparseableBodyIsLoggedAndRethrown()
    {
        var logger = new RecordingLogger<GeoapifyClient>();
        var handler = StubHttpMessageHandler.Returning("this is not json");

        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => CreateSut(handler, logger).GetDestinationDetailsAsync("geo-1"));

        Assert.Contains("place details", Assert.Single(logger.Warnings).Message);
    }

    [Fact]
    public async Task TheApiKeyIsNeverWrittenToTheLog()
    {
        var logger = new RecordingLogger<GeoapifyClient>();
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("boom"));

        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => CreateSut(handler, logger).SearchLocationsAsync("hanoi"));

        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains(ApiKey));
    }

    [Fact]
    public async Task ACancelledRequestIsNotTreatedAsAProviderFailure()
    {
        // The caller walked away; that is not a Geoapify outage and logging it as
        // one would put noise in the log for every abandoned search.
        var logger = new RecordingLogger<GeoapifyClient>();
        var handler = StubHttpMessageHandler.Throwing(new TaskCanceledException("cancelled"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => CreateSut(handler, logger).SearchLocationsAsync("hanoi", cts.Token));

        Assert.Empty(logger.Warnings);
    }
}
