using TripPlanner.Application.Features.Trips;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Persistence for the Trip aggregate (Trip + ItineraryDay + ItineraryItem).
/// Days and items have no meaning outside the trip that owns them, so they are
/// reached through the trip rather than through repositories of their own, and a
/// single save covers the whole graph.
/// </summary>
public interface ITripRepository
{
    /// <summary>Projected list rows for GetMyTripsAsync — see TripMappings.ToSummaryRowExpression.</summary>
    Task<List<TripSummaryRow>> GetSummaryRowsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Read-only, full graph (Days.Items.Destination + Items.Destination) — for GetTripAsync.</summary>
    Task<Trip?> GetDetailsAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Full graph loaded for mutation; changes to Days/Items are persisted by UpdateAsync.</summary>
    Task<Trip?> GetForUpdateAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Whether the given day belongs to the given trip (checked before scheduling into it).</summary>
    Task<bool> DayBelongsToTripAsync(Guid itineraryDayId, Guid tripId, CancellationToken cancellationToken = default);

    /// <summary>The item, only if it belongs to a trip owned by userId (NFR 6).</summary>
    Task<ItineraryItem?> GetOwnedItemAsync(Guid tripId, Guid itemId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Inserts the trip and persists immediately.</summary>
    Task AddAsync(Trip trip, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a new day for insertion, without saving — call alongside adding it to
    /// <c>trip.Days</c>, then persist the whole aggregate with <see cref="UpdateAsync"/>.
    ///
    /// Required because <c>BaseEntity</c> self-assigns its Guid key, so EF sees a
    /// non-default key on a newly discovered child and classifies it as an existing
    /// row (UPDATE) rather than a new one (INSERT).
    /// </summary>
    void AddDay(ItineraryDay day);

    /// <summary>Stages a new item for insertion, without saving. See <see cref="AddDay"/>.</summary>
    void AddItem(ItineraryItem item);

    /// <summary>Persists every pending change to the trip and its days/items in one save.</summary>
    Task UpdateAsync(Trip trip, CancellationToken cancellationToken = default);

    /// <summary>Deletes a single itinerary item and persists immediately.</summary>
    Task RemoveItemAsync(ItineraryItem item, CancellationToken cancellationToken = default);
}
