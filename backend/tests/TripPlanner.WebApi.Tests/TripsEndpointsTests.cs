using System.Net;
using System.Net.Http.Json;
using TripPlanner.Application.Features.Trips.Dtos;
using Xunit;

namespace TripPlanner.WebApi.Tests;

/// <summary>
/// HTTP-level tests for TripsController: the [Authorize] gate and per-user
/// ownership filtering (NFR 6) are both WebApi/middleware concerns that the
/// Application-layer TripServiceTests can't exercise, since those call
/// ITripService directly with a hand-picked ICurrentUserService.
/// </summary>
public class TripsEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TripsEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetMyTrips_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/trips");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateTrip_ThenGetIt_RoundTrips()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-roundtrip"));

        var createResponse = await client.PostAsJsonAsync("/api/trips", new CreateTripRequest("Japan 2026"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TripSummaryDto>();
        Assert.NotNull(created);
        Assert.Equal("Japan 2026", created!.Name);

        var getResponse = await client.GetAsync(createResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var detail = await getResponse.Content.ReadFromJsonAsync<TripDetailDto>();
        Assert.Equal(created.Id, detail!.Id);
    }

    [Fact]
    public async Task GetTrip_UnknownId_ReturnsNotFound()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-unknown"));

        var response = await client.GetAsync($"/api/trips/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTrip_BelongingToAnotherUser_ReturnsNotFound()
    {
        var (ownerClient, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-owner"));
        var createResponse = await ownerClient.PostAsJsonAsync("/api/trips", new CreateTripRequest("Owner's trip"));
        var trip = await createResponse.Content.ReadFromJsonAsync<TripSummaryDto>();

        var (otherClient, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-intruder"));

        var response = await otherClient.GetAsync($"/api/trips/{trip!.Id}");

        // Ownership is enforced by filtering the query itself (never "does this
        // id exist, then check owner"), so a stranger sees 404, not 403 — it
        // shouldn't even be observable that the trip exists.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMyTrips_OnlyReturnsCallersOwnTrips()
    {
        var (userAClient, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-list-a"));
        await userAClient.PostAsJsonAsync("/api/trips", new CreateTripRequest("A's trip"));

        var (userBClient, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-list-b"));
        await userBClient.PostAsJsonAsync("/api/trips", new CreateTripRequest("B's trip 1"));
        await userBClient.PostAsJsonAsync("/api/trips", new CreateTripRequest("B's trip 2"));

        var response = await userBClient.GetAsync("/api/trips");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trips = await response.Content.ReadFromJsonAsync<List<TripSummaryDto>>();
        Assert.Equal(2, trips!.Count);
        Assert.All(trips, t => Assert.StartsWith("B's trip", t.Name));
    }

    [Fact]
    public async Task CreateTrip_MissingName_ReturnsBadRequest()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-invalid"));

        var response = await client.PostAsJsonAsync("/api/trips", new CreateTripRequest(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTrip_RenameAndSetDates_RegeneratesItineraryDays()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-update"));
        var tripId = await CreateTripAsync(client, "Original name");
        var start = new DateOnly(2026, 3, 10);
        var end = new DateOnly(2026, 3, 12); // 3-day range

        var response = await client.PutAsJsonAsync($"/api/trips/{tripId}", new UpdateTripRequest("Renamed trip", start, end));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<TripDetailDto>();
        Assert.Equal("Renamed trip", detail!.Name);
        Assert.Equal(start, detail.StartDate);
        Assert.Equal(end, detail.EndDate);
        // US2 day regeneration: a 3-day range produces 3 ItineraryDay rows, numbered 1..3.
        Assert.Equal(3, detail.Days.Count);
        Assert.Equal(new[] { 1, 2, 3 }, detail.Days.OrderBy(d => d.DayNumber).Select(d => d.DayNumber));
    }

    [Fact]
    public async Task UpdateTrip_EmptyName_ReturnsBadRequest()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-update-invalid"));
        var tripId = await CreateTripAsync(client, "Trip");

        var response = await client.PutAsJsonAsync($"/api/trips/{tripId}", new UpdateTripRequest("", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTrip_BelongingToAnotherUser_ReturnsNotFound()
    {
        var (ownerClient, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-update-owner"));
        var tripId = await CreateTripAsync(ownerClient, "Owner's trip");
        var (otherClient, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-update-intruder"));

        var response = await otherClient.PutAsJsonAsync($"/api/trips/{tripId}", new UpdateTripRequest("Hijacked", null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddDestination_ThenRemove_RoundTrips()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-add-remove"));
        var tripId = await CreateTripAsync(client, "Add/remove trip");

        var added = await AddDestinationAsync(client, tripId, "fake-place-1");
        Assert.Equal("Fake Place fake-place-1", added.Name);

        var afterAdd = await GetTripDetailAsync(client, tripId);
        Assert.Single(afterAdd.SavedPlaces);

        var removeResponse = await client.DeleteAsync($"/api/trips/{tripId}/destinations/{added.ItemId}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var afterRemove = await GetTripDetailAsync(client, tripId);
        Assert.Empty(afterRemove.SavedPlaces);
    }

    [Fact]
    public async Task RemoveDestination_UnknownItem_ReturnsNotFound()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-remove-unknown"));
        var tripId = await CreateTripAsync(client, "Trip");

        var response = await client.DeleteAsync($"/api/trips/{tripId}/destinations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveDestination_BelongingToAnotherUser_ReturnsNotFound()
    {
        var (ownerClient, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-remove-owner"));
        var tripId = await CreateTripAsync(ownerClient, "Owner's trip");
        var item = await AddDestinationAsync(ownerClient, tripId, "fake-place-owner");
        var (otherClient, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-remove-intruder"));

        var response = await otherClient.DeleteAsync($"/api/trips/{tripId}/destinations/{item.ItemId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItineraryItem_ChangeSortOrder_ReordersSavedPlaces()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-reorder"));
        var tripId = await CreateTripAsync(client, "Reorder trip");
        var first = await AddDestinationAsync(client, tripId, "fake-a"); // SortOrder 0
        var second = await AddDestinationAsync(client, tripId, "fake-b"); // SortOrder 1

        // Move the second-added item to the front.
        var reorderResponse = await client.PutAsJsonAsync(
            $"/api/trips/{tripId}/destinations/{second.ItemId}", new UpdateItineraryItemRequest(null, 0));

        Assert.Equal(HttpStatusCode.OK, reorderResponse.StatusCode);
        var detail = await GetTripDetailAsync(client, tripId);
        var ordered = detail.SavedPlaces.OrderBy(d => d.SortOrder).ToList();
        Assert.Equal(second.ItemId, ordered[0].ItemId);
        Assert.Equal(first.ItemId, ordered[1].ItemId);
    }

    [Fact]
    public async Task UpdateItineraryItem_DayNotOnTrip_ReturnsBadRequest()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync(AuthTestHelper.UniqueEmail("trips-move-badday"));
        var tripId = await CreateTripAsync(client, "Trip");
        var item = await AddDestinationAsync(client, tripId, "fake-c");

        // A day id that doesn't belong to (or exist on) this trip.
        var response = await client.PutAsJsonAsync(
            $"/api/trips/{tripId}/destinations/{item.ItemId}", new UpdateItineraryItemRequest(Guid.NewGuid(), 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<Guid> CreateTripAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/trips", new CreateTripRequest(name));
        var trip = await response.Content.ReadFromJsonAsync<TripSummaryDto>();
        return trip!.Id;
    }

    private static async Task<TripDestinationDto> AddDestinationAsync(HttpClient client, Guid tripId, string providerId, Guid? itineraryDayId = null)
    {
        var response = await client.PostAsJsonAsync($"/api/trips/{tripId}/destinations", new AddDestinationRequest(providerId, itineraryDayId));
        return (await response.Content.ReadFromJsonAsync<TripDestinationDto>())!;
    }

    private static async Task<TripDetailDto> GetTripDetailAsync(HttpClient client, Guid tripId)
    {
        var response = await client.GetAsync($"/api/trips/{tripId}");
        return (await response.Content.ReadFromJsonAsync<TripDetailDto>())!;
    }
}
