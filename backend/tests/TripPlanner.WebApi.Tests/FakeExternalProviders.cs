using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Destinations.Dtos;

namespace TripPlanner.WebApi.Tests;

/// <summary>
/// Deterministic stand-ins for the real Geoapify/Serper HttpClients, registered
/// by <see cref="CustomWebApplicationFactory"/> so destination-related tests
/// don't depend on network access or a real API key. GetDestinationDetailsAsync
/// echoes the requested id back as the Destination cache key (matching
/// GeoapifyClient's own contract — see its doc comment).
/// </summary>
public class FakeDestinationProvider : IDestinationProvider
{
    public Task<IReadOnlyList<LocationSuggestionDto>> SearchLocationsAsync(string query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LocationSuggestionDto>>([]);

    public Task<IReadOnlyList<DestinationSummaryDto>> GetAttractionsAsync(double latitude, double longitude, double radiusKm, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DestinationSummaryDto>>([]);

    public Task<DestinationDetailsDto?> GetDestinationDetailsAsync(string providerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<DestinationDetailsDto?>(new DestinationDetailsDto(
            ProviderId: providerId,
            Name: $"Fake Place {providerId}",
            Category: "landmark",
            Description: "A fake destination for tests.",
            ImageUrl: "https://example.com/fake.png",
            Latitude: 35.0,
            Longitude: 139.0,
            Address: "1 Fake Street",
            Website: null,
            OpeningHours: null));
}

public class FakeImageSearchProvider : IImageSearchProvider
{
    public Task<string?> SearchImageAsync(string query, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<IReadOnlyList<string>> SearchImagesAsync(string query, int maxResults, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
