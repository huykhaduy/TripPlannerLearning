using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
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
    private readonly GeoapifySettings _settings;

    public GeoapifyClient(HttpClient httpClient, IOptions<GeoapifySettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
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

    public async Task<DestinationDetailsDto?> GetDestinationDetailsAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var url = $"v2/place-details?id={Uri.EscapeDataString(providerId)}&apiKey={_settings.ApiKey}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);

        // Geoapify answers 400/404 for ids it does not recognise — for us that
        // simply means "no such destination", which the contract expresses as null.
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode(); // anything else (401, 5xx) is a real fault

        var payload = await response.Content.ReadFromJsonAsync<PlaceDetailsResponse>(cancellationToken);
        var place = payload?.Features?.FirstOrDefault()?.Properties;
        if (place is null)
        {
            return null; // well-formed response but no matching place
        }

        return new DestinationDetailsDto(
            // Always echo the REQUESTED id, not the (sometimes different)
            // canonical place_id in the response — it is the Destination cache
            // key, and repeat adds must hit the same row.
            ProviderId: providerId,
            Name: place.Name ?? place.AddressLine1 ?? "Unnamed place",
            Category: place.Categories?.FirstOrDefault(),
            Description: place.Description,
            ImageUrl: place.WikiAndMedia?.Image,
            Latitude: place.Lat,
            Longitude: place.Lon,
            Address: place.Formatted,
            Website: place.Website,
            OpeningHours: place.OpeningHours);
    }

    // ------------------------------------------------------------------
    // Private wire DTOs — the exact JSON shapes Geoapify returns (GeoJSON
    // FeatureCollection). Kept private: the rest of the app only ever sees
    // the Application-layer DTOs mapped above.
    // ------------------------------------------------------------------

    private sealed record PlaceDetailsResponse(
        [property: JsonPropertyName("features")] List<PlaceFeature>? Features);

    private sealed record PlaceFeature(
        [property: JsonPropertyName("properties")] PlaceProperties? Properties);

    private sealed record PlaceProperties(
        [property: JsonPropertyName("place_id")] string? PlaceId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("address_line1")] string? AddressLine1,
        [property: JsonPropertyName("formatted")] string? Formatted,
        [property: JsonPropertyName("categories")] List<string>? Categories,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("website")] string? Website,
        [property: JsonPropertyName("opening_hours")] string? OpeningHours,
        [property: JsonPropertyName("lat")] double? Lat,
        [property: JsonPropertyName("lon")] double? Lon,
        [property: JsonPropertyName("wiki_and_media")] WikiAndMedia? WikiAndMedia);

    private sealed record WikiAndMedia(
        [property: JsonPropertyName("image")] string? Image);
}
