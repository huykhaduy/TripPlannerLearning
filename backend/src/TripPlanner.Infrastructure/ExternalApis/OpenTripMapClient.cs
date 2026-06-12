using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Destinations.Dtos;

namespace TripPlanner.Infrastructure.ExternalApis;

/// <summary>
/// STUB — students implement this (Features 1 &amp; 2).
///
/// This is the concrete <see cref="IDestinationProvider"/>. It receives a typed
/// <see cref="HttpClient"/> (configured in DependencyInjection) and should call
/// OpenTripMap (geocoding + POIs) and optionally Foursquare to enrich results.
///
/// Tips:
///   * read the API key from configuration (never hard-code secrets);
///   * use System.Text.Json to deserialize responses into private DTOs, then
///     map to the public DTOs in Application;
///   * handle missing fields gracefully — many places lack images/ratings.
///
/// Get a free OpenTripMap key at https://opentripmap.io/product
/// </summary>
public class OpenTripMapClient : IDestinationProvider
{
    private readonly HttpClient _httpClient;

    public OpenTripMapClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<IReadOnlyList<LocationSuggestionDto>> SearchLocationsAsync(string query, CancellationToken cancellationToken = default)
    {
        // TODO: call OpenTripMap geocoding (`/geoname`) and map to suggestions.
        throw new NotImplementedException("Call the OpenTripMap geocoding API — see Feature 1, US1/US2.");
    }

    public Task<IReadOnlyList<DestinationSummaryDto>> GetAttractionsAsync(double latitude, double longitude, double radiusKm, CancellationToken cancellationToken = default)
    {
        // TODO: call OpenTripMap `/radius` for POIs near the coordinate.
        throw new NotImplementedException("Call the OpenTripMap POI API — see Feature 1, US3.");
    }

    public Task<DestinationDetailsDto?> GetDestinationDetailsAsync(string providerId, CancellationToken cancellationToken = default)
    {
        // TODO: call OpenTripMap `/xid/{id}` (and/or Foursquare) for full details.
        throw new NotImplementedException("Call the OpenTripMap details API — see Feature 2, US1.");
    }
}
