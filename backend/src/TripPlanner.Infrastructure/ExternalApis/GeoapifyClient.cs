using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Destinations.Dtos;

namespace TripPlanner.Infrastructure.ExternalApis;

/// <summary>
/// STUB — students implement this (Features 1 &amp; 2).
///
/// This is the concrete <see cref="IDestinationProvider"/>. It receives a typed
/// <see cref="HttpClient"/> (configured in DependencyInjection, base address
/// https://api.geoapify.com/) and should call the Geoapify Geocoding and Places
/// APIs (https://apidocs.geoapify.com/docs).
///
/// Endpoint map:
///   * SearchLocationsAsync        → GET v1/geocode/autocomplete?text={query}&amp;type=city&amp;limit=5&amp;apiKey={key}
///   * GetAttractionsAsync         → GET v2/places?categories=tourism.sights,tourism.attraction
///                                        &amp;filter=circle:{lon},{lat},{radiusMeters}&amp;limit=20&amp;apiKey={key}
///                                     (note: Geoapify wants LON before LAT, and radius in METERS)
///   * GetDestinationDetailsAsync  → GET v2/place-details?id={placeId}&amp;apiKey={key}
///
/// Tips:
///   * read the API key from configuration ("Geoapify:ApiKey") — never hard-code secrets;
///   * use System.Text.Json to deserialize responses into private DTOs, then
///     map to the public DTOs in Application;
///   * handle missing fields gracefully — Geoapify has no ratings and few images,
///     so Rating/ImageUrl will often be null (the UI shows placeholders).
///
/// Get a free Geoapify key at https://myprojects.geoapify.com/
/// </summary>
public class GeoapifyClient : IDestinationProvider
{
    private readonly HttpClient _httpClient;

    public GeoapifyClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<IReadOnlyList<LocationSuggestionDto>> SearchLocationsAsync(string query, CancellationToken cancellationToken = default)
    {
        // TODO: call Geoapify geocode autocomplete (`v1/geocode/autocomplete`) and map to suggestions.
        throw new NotImplementedException("Call the Geoapify geocoding API — see Feature 1, US1/US2.");
    }

    public Task<IReadOnlyList<DestinationSummaryDto>> GetAttractionsAsync(double latitude, double longitude, double radiusKm, CancellationToken cancellationToken = default)
    {
        // TODO: call Geoapify Places (`v2/places`) with a circle filter for POIs near the coordinate.
        throw new NotImplementedException("Call the Geoapify Places API — see Feature 1, US3.");
    }

    public Task<DestinationDetailsDto?> GetDestinationDetailsAsync(string providerId, CancellationToken cancellationToken = default)
    {
        // TODO: call Geoapify Place Details (`v2/place-details?id={providerId}`) for full details.
        throw new NotImplementedException("Call the Geoapify place-details API — see Feature 2, US1.");
    }
}
