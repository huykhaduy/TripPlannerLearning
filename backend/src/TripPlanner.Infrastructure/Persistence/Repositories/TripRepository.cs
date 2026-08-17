using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Trips;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Repositories;

public class TripRepository : ITripRepository
{
    private readonly ApplicationDbContext _context;

    public TripRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TripSummaryRow>> GetSummaryRowsForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await _context.Trips
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(TripMappings.ToSummaryRowExpression)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// AsSplitQuery because Days and Items are both collections off Trip: in one
    /// statement they multiply into (items + empty days) × items rows — 900 for a
    /// 44-record trip, 3600 for 90 — with every item materialised twice.
    ///
    /// A trade-off, not a free win: three round trips cost a flat ~3.5 ms, so below
    /// ~10-15 items the single query is the faster one. Chosen because that loss has
    /// a ceiling and the win does not. (Split reads are also non-atomic without an
    /// explicit transaction — fine here, a trip is only written by its owner.)
    ///
    /// EF InMemory ignores AsSplitQuery, so no test covers this. Re-measure with
    /// tests/TripPlanner.QueryBenchmarks if this query or the database host changes.
    /// </summary>
    public async Task<Trip?> GetDetailsAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
        await _context.Trips
            .AsNoTracking()
            .AsSplitQuery()
            .Include(t => t.Days).ThenInclude(d => d.Items).ThenInclude(i => i.Destination)
            .Include(t => t.Items).ThenInclude(i => i.Destination)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, cancellationToken);

    /// <summary>Same two-collection cartesian product as GetDetailsAsync — see there.</summary>
    public async Task<Trip?> GetForUpdateAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
        await _context.Trips
            .AsSplitQuery()
            .Include(t => t.Days).ThenInclude(d => d.Items)
            .Include(t => t.Items).ThenInclude(i => i.Destination)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, cancellationToken);

    public async Task<bool> DayBelongsToTripAsync(Guid itineraryDayId, Guid tripId, CancellationToken cancellationToken = default) =>
        await _context.ItineraryDays
            .AnyAsync(d => d.Id == itineraryDayId && d.TripId == tripId, cancellationToken);

    public async Task<ItineraryItem?> GetOwnedItemAsync(Guid tripId, Guid itemId, Guid userId, CancellationToken cancellationToken = default) =>
        await _context.ItineraryItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.TripId == tripId && i.Trip!.UserId == userId, cancellationToken);

    public async Task AddAsync(Trip trip, CancellationToken cancellationToken = default)
    {
        _context.Trips.Add(trip);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Only flushes — the graph is already tracked and EF writes it in one
    /// transaction. Two things the signature hides:
    ///
    /// 1. <paramref name="trip"/> is unused, and this works ONLY for a trip from
    ///    GetForUpdateAsync. Pass one from GetDetailsAsync (AsNoTracking) and nothing
    ///    is written — no UPDATE, no exception, no log.
    /// 2. It flushes everything the scoped DbContext tracks, not just this trip —
    ///    hence the ORDER MATTERS note in TripService.AddDestinationAsync.
    /// </summary>
    public async Task UpdateAsync(Trip trip, CancellationToken cancellationToken = default) =>
        await _context.SaveChangesAsync(cancellationToken);

    public async Task RemoveItemAsync(ItineraryItem item, CancellationToken cancellationToken = default)
    {
        _context.ItineraryItems.Remove(item);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
