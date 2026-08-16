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
    /// Split into one query per collection. Days and Items are BOTH collections
    /// hanging off Trip, so a single-query include LEFT JOINs each of them onto the
    /// trip row independently — and the two join products multiply.
    ///
    /// The blow-up is QUADRATIC IN THE ITEM COUNT, not days × items: the first join
    /// yields one row per (day, item-in-that-day) plus one per empty day, and the
    /// second multiplies all of that by every item in the trip. Measured against
    /// SQLite with this exact query: 30 items over 14 days = 900 rows; 60 items over
    /// 30 days = 3600 rows. On top of that, under AsNoTracking every item is
    /// materialised twice — once under its day, once under Trip.Items.
    ///
    /// The trade-off: split queries are several round trips and are NOT atomic
    /// unless wrapped in an explicit transaction, so a concurrent write could in
    /// principle land between them. Accepted here — a trip is only ever written by
    /// its own owner, and the two mutating paths save inside one transaction anyway.
    ///
    /// Note that no test covers this: AsSplitQuery is a relational-only API and the
    /// EF InMemory provider used by the suite ignores it, so the behaviour has to be
    /// reasoned about against Npgsql directly (same caveat as the unique-index
    /// translation in ApplicationDbContext).
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
    /// The trip and its days/items are already tracked, so this only needs to flush —
    /// EF writes the whole graph in one transaction.
    ///
    /// Two things the signature does not say, and both matter:
    ///
    /// 1. The <paramref name="trip"/> argument is deliberately unused. The change
    ///    tracker already knows the entity; the parameter is here so the call site
    ///    reads as "save this trip" rather than a bare SaveChanges. The corollary is
    ///    that this ONLY works for a trip loaded by a TRACKING query, i.e.
    ///    GetForUpdateAsync. Hand it one from GetDetailsAsync (AsNoTracking) and
    ///    nothing is written — no UPDATE, no exception, no log.
    ///
    /// 2. It flushes everything the scoped DbContext is tracking, not just this
    ///    trip — every repository shares one context. That is why
    ///    TripService.AddDestinationAsync must insert its Destination BEFORE it
    ///    touches the trip graph; see the ORDER MATTERS note there.
    /// </summary>
    public async Task UpdateAsync(Trip trip, CancellationToken cancellationToken = default) =>
        await _context.SaveChangesAsync(cancellationToken);

    public async Task RemoveItemAsync(ItineraryItem item, CancellationToken cancellationToken = default)
    {
        _context.ItineraryItems.Remove(item);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
