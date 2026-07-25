using TripPlanner.Application.Features.Trips;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Trip is the aggregate root for the trip-planning feature; queries that
/// enforce invariants across Trip/ItineraryDay/ItineraryItem (ownership,
/// "does this day belong to this trip") live here rather than on the generic
/// IRepository&lt;ItineraryDay&gt;/IRepository&lt;ItineraryItem&gt;.
/// </summary>
public interface ITripRepository : IRepository<Trip>
{
    /// <summary>Projected list rows for GetMyTripsAsync — see TripMappings.ToSummaryRowExpression.</summary>
    Task<List<TripSummaryRow>> GetSummaryRowsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Read-only, full graph (Days.Items.Destination + Items.Destination) — for GetTripAsync.</summary>
    Task<Trip?> GetDetailsAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Tracked, full graph (Days.Items + Items.Destination) — for any write path needing the trip's days and/or items.</summary>
    Task<Trip?> GetTrackedWithFullGraphAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Whether the given day belongs to the given trip (checked before scheduling a destination into it).</summary>
    Task<bool> DayBelongsToTripAsync(Guid itineraryDayId, Guid tripId, CancellationToken cancellationToken = default);

    /// <summary>The item, tracked, only if it belongs to a trip owned by userId (NFR 6) — used by RemoveDestinationAsync.</summary>
    Task<ItineraryItem?> GetOwnedItemAsync(Guid tripId, Guid itemId, Guid userId, CancellationToken cancellationToken = default);
}
