using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Destinations.Dtos;

namespace TripPlanner.Application.Features.Destinations;

/// <summary>
/// STUB — students implement this (Features 1 &amp; 2).
///
/// Suggested approach:
///   * call <see cref="IDestinationProvider"/> for raw data;
///   * de-duplicate, rank by relevance, cap results (F1 business rules);
///   * optionally cache popular searches to meet the performance NFRs;
///   * map provider models to the DTOs in this folder.
///
/// Every method currently throws <see cref="NotImplementedException"/> so the
/// project compiles and the API surface is visible in Swagger from day one.
/// </summary>
public class DestinationService : IDestinationService
{
    private readonly IDestinationProvider _provider;

    public DestinationService(IDestinationProvider provider)
    {
        _provider = provider;
    }

    public Task<IReadOnlyList<LocationSuggestionDto>> SearchLocationsAsync(string query, CancellationToken cancellationToken = default)
    {
        // TODO: F1/US1-US2 — validate query (min length), call provider, de-dupe,
        //       rank exact matches first, return at most 5 results.
        throw new NotImplementedException("Implement location search — see Feature 1, US1 & US2.");
    }

    public Task<IReadOnlyList<DestinationSummaryDto>> GetAttractionsAsync(double latitude, double longitude, double radiusKm, CancellationToken cancellationToken = default)
    {
        // TODO: F1/US3 — fetch POIs near the coordinate, cap at 20 per page,
        //       order by popularity/rating, supply placeholders for missing data.
        throw new NotImplementedException("Implement recommended attractions — see Feature 1, US3.");
    }

    public Task<DestinationDetailsDto> GetDetailsAsync(string providerId, CancellationToken cancellationToken = default)
    {
        // TODO: F2/US1 — fetch full details by provider id; the view must still
        //       open when optional fields (photos/hours/map) are missing.
        throw new NotImplementedException("Implement destination details — see Feature 2, US1.");
    }
}
