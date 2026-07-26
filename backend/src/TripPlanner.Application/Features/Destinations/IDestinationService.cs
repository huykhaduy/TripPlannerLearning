using TripPlanner.Application.Features.Destinations.Dtos;

namespace TripPlanner.Application.Features.Destinations;

/// <summary>
/// Search and detail use-cases for destinations (Features 1 &amp; 2). Implemented
/// by <see cref="DestinationService"/>, which calls
/// <see cref="Common.Interfaces.IDestinationProvider"/> for the external data and
/// caches results to satisfy the NFRs (≤500ms search, ≤1000ms attractions).
/// </summary>
public interface IDestinationService
{
    Task<IReadOnlyList<LocationSuggestionDto>> SearchLocationsAsync(string query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DestinationSummaryDto>> GetAttractionsAsync(double latitude, double longitude, double radiusKm, CancellationToken cancellationToken = default);

    Task<DestinationDetailsDto> GetDetailsAsync(string providerId, CancellationToken cancellationToken = default);
}
