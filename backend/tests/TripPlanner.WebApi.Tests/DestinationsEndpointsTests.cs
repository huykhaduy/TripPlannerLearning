using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using TripPlanner.Application.Features.Destinations.Dtos;
using TripPlanner.Application.Features.Destinations.Validators;
using Xunit;

namespace TripPlanner.WebApi.Tests;

/// <summary>
/// HTTP-level tests for DestinationsController. Three things here can only be
/// checked through the pipeline, not through DestinationServiceTests:
///
///   * these endpoints are deliberately PUBLIC (no [Authorize]) so a visitor can
///     browse before signing up (F3/US8) — a stray [Authorize] would break the
///     landing experience and nothing else would catch it;
///   * query-string binding, including the radiusKm default;
///   * ValidationException/NotFoundException reaching the client as 400/404
///     ProblemDetails via ExceptionHandlingMiddleware.
///
/// The provider is <see cref="FakeDestinationProvider"/>, so search and
/// attractions always come back empty — these assert the contract (status, shape,
/// binding), while the ranking/dedupe/caching rules are covered as unit tests.
/// </summary>
public class DestinationsEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DestinationsEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ---------------------------------------------------------------------
    // Public access (F3/US8)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("/api/destinations/locations?query=paris")]
    [InlineData("/api/destinations/attractions?lat=48.85&lng=2.35")]
    [InlineData("/api/destinations/some-provider-id")]
    public async Task AllEndpoints_WithoutAToken_AreReachable(string url)
    {
        var client = _factory.CreateClient(); // no Authorization header

        var response = await client.GetAsync(url);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------------
    // GET /api/destinations/locations
    // ---------------------------------------------------------------------

    [Fact]
    public async Task SearchLocations_WithValidQuery_ReturnsJsonArray()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/destinations/locations?query=paris");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<LocationSuggestionDto>>();
        Assert.NotNull(results); // the fake provider knows no locations, so this is empty
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchLocations_WithQueryShorterThanMinimum_ReturnsBadRequestWithFieldError()
    {
        var client = _factory.CreateClient();

        // MinQueryLength is 2, so a single character must be rejected.
        var response = await client.GetAsync("/api/destinations/locations?query=a");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.True(problem!.Extensions.ContainsKey("errors"));
    }

    [Fact]
    public async Task SearchLocations_WithWhitespaceOnlyQuery_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        // The TRIMMED query is what gets validated, so padding must not sneak past.
        var response = await client.GetAsync("/api/destinations/locations?query=%20%20");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SearchLocations_WithNoQueryParameter_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        // query binds to null — must be a 400, not an unhandled 500.
        var response = await client.GetAsync("/api/destinations/locations");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------------
    // GET /api/destinations/attractions
    // ---------------------------------------------------------------------

    [Fact]
    public async Task GetAttractions_WithoutRadius_UsesTheDefaultAndSucceeds()
    {
        var client = _factory.CreateClient();

        // radiusKm is an optional query parameter defaulting to 20; omitting it must
        // bind to that default rather than 0, which the validator would reject.
        var response = await client.GetAsync("/api/destinations/attractions?lat=48.85&lng=2.35");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<DestinationSummaryDto>>();
        Assert.NotNull(results);
    }

    [Theory]
    [InlineData(91, 2.35)]     // latitude above +90
    [InlineData(-91, 2.35)]    // latitude below -90
    [InlineData(48.85, 181)]   // longitude above +180
    [InlineData(48.85, -181)]  // longitude below -180
    public async Task GetAttractions_WithCoordinatesOutOfRange_ReturnsBadRequest(double lat, double lng)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            FormattableString.Invariant($"/api/destinations/attractions?lat={lat}&lng={lng}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]   // must be greater than 0
    [InlineData(-5)]
    [InlineData(GetAttractionsRequestValidator.MaxRadiusKm + 1)]
    public async Task GetAttractions_WithRadiusOutOfRange_ReturnsBadRequest(double radiusKm)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            FormattableString.Invariant($"/api/destinations/attractions?lat=48.85&lng=2.35&radiusKm={radiusKm}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------------
    // GET /api/destinations/{providerId}
    // ---------------------------------------------------------------------

    [Fact]
    public async Task GetDetails_ForAKnownProviderId_ReturnsTheDestination()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/destinations/geo-123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = await response.Content.ReadFromJsonAsync<DestinationDetailsDto>();
        // The requested id is echoed back, not the provider's canonical one — it's the
        // Destination cache key, so repeat adds must resolve to the same row.
        Assert.Equal("geo-123", details!.ProviderId);
        Assert.Equal("Fake Place geo-123", details.Name);
    }

    [Fact]
    public async Task GetDetails_WhenNeitherProviderNorDatabaseKnowsIt_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        // The provider reports this id as unknown, and it was never added to a trip,
        // so the DB fallback misses too — only then is it a 404 (spec §8.4).
        var response = await client.GetAsync($"/api/destinations/{FakeDestinationProvider.UnknownIdPrefix}nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
