using TripPlanner.Application.Features.Destinations.Dtos;

namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Abstraction over the external travel data provider(s) — e.g. Geoapify for
/// geocoding/POIs (Features 1 &amp; 2), implemented by <c>GeoapifyClient</c> in
/// Infrastructure. Keeping it behind an interface means the search use-cases
/// never depend on a specific vendor and can be unit-tested with a fake provider.
/// </summary>
public interface IDestinationProvider
{
    /// <summary>F1/US1 &amp; US2 — autocomplete / search cities &amp; countries by text.</summary>
    Task<IReadOnlyList<LocationSuggestionDto>> SearchLocationsAsync(
        string query, CancellationToken cancellationToken = default);

    /// <summary>F1/US3 — recommended attractions near a coordinate.</summary>
    Task<IReadOnlyList<DestinationSummaryDto>> GetAttractionsAsync(
        double latitude, double longitude, double radiusKm, CancellationToken cancellationToken = default);

    /// <summary>F2/US1 — full details for a single place by provider id.</summary>
    Task<DestinationDetailsDto?> GetDestinationDetailsAsync(
        string providerId, CancellationToken cancellationToken = default);
}
