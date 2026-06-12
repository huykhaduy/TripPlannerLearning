namespace TripPlanner.Application.Features.Trips.Dtos;

/// <summary>F3/US1 — create a trip (name is required).</summary>
public record CreateTripRequest(string Name);

/// <summary>F3/US2 &amp; US9 — rename a trip and/or change its dates.</summary>
public record UpdateTripRequest(string Name, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>F3/US3 — add a destination to a trip, optionally into a specific day.</summary>
public record AddDestinationRequest(string ProviderId, Guid? ItineraryDayId);

/// <summary>Summary row for the user's trip list (F3/US10).</summary>
public record TripSummaryDto(Guid Id, string Name, DateOnly? StartDate, DateOnly? EndDate, int DestinationCount);

/// <summary>A destination as it appears inside a trip.</summary>
public record TripDestinationDto(Guid ItemId, string ProviderId, string Name, string? ImageUrl, int SortOrder);

/// <summary>One day of the itinerary with its scheduled destinations.</summary>
public record ItineraryDayDto(Guid Id, DateOnly Date, int DayNumber, IReadOnlyList<TripDestinationDto> Destinations);

/// <summary>Full trip detail: the day-by-day plan plus the unscheduled "Saved Places".</summary>
public record TripDetailDto(
    Guid Id,
    string Name,
    DateOnly? StartDate,
    DateOnly? EndDate,
    IReadOnlyList<ItineraryDayDto> Days,
    IReadOnlyList<TripDestinationDto> SavedPlaces);
