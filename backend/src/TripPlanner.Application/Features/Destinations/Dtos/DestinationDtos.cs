namespace TripPlanner.Application.Features.Destinations.Dtos;

/// <summary>F1/US1 &amp; US2 — the search query, wrapped so it can be validated.</summary>
public record SearchLocationsRequest(string Query);

/// <summary>F1/US3 — attraction lookup inputs, wrapped so they can be validated.</summary>
public record GetAttractionsRequest(double Latitude, double Longitude, double RadiusKm);

/// <summary>F2/US1 — details lookup input, wrapped so it can be validated.</summary>
public record GetDestinationDetailsRequest(string ProviderId);

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
/// <param name="ImageUrl">The primary/hero photo — first of <see cref="ImageUrls"/>, kept for callers that only need a single thumbnail (e.g. "Add to trip").</param>
/// <param name="ImageUrls">F2/US2 — the photo gallery shown in the details view's carousel. Empty (not null) when nothing was found.</param>
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
    string? OpeningHours,
    IReadOnlyList<string>? ImageUrls = null);
