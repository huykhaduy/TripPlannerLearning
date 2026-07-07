namespace TripPlanner.Application.Features.Destinations.Dtos;

/// <summary>F1/US1 &amp; US2 — the search query, wrapped so it can be validated.</summary>
public record SearchLocationsRequest(string Query);

/// <summary>F1/US3 — attraction lookup inputs, wrapped so they can be validated.</summary>
public record GetAttractionsRequest(double Latitude, double Longitude, double RadiusKm);

/// <summary>F1/US1 &amp; US2 — a city/country autocomplete suggestion.</summary>
public record LocationSuggestionDto(string Name, string? Country, double Latitude, double Longitude);

/// <summary>F1/US3 — a single attraction in the recommended list (lightweight).</summary>
public record DestinationSummaryDto(
    string ProviderId,
    string Name,
    string? Category,
    string? ImageUrl,
    double? Rating);

/// <summary>F2/US1 — full details shown in the destination detail view.</summary>
public record DestinationDetailsDto(
    string ProviderId,
    string Name,
    string? Category,
    string? Description,
    string? ImageUrl,
    double? Latitude,
    double? Longitude,
    string? Address,
    string? Website,
    string? OpeningHours);
