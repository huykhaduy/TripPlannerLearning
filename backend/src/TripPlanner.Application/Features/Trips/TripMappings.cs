using TripPlanner.Application.Features.Trips.Dtos;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Features.Trips;

/// <summary>
/// Entity → DTO mapping for the Trips feature, kept in one place so every
/// service method returns identically-shaped data. Extend this class as the
/// remaining DTOs (TripDetailDto, ItineraryDayDto, ...) come into play.
/// </summary>
public static class TripMappings
{
    /// <summary>
    /// Requires <c>trip.Items</c> to be loaded (or the trip to be freshly
    /// created); otherwise the destination count would silently read 0.
    /// </summary>
    public static TripSummaryDto ToSummaryDto(this Trip trip) =>
        new(trip.Id, trip.Name, trip.StartDate, trip.EndDate, trip.Items.Count);
}
