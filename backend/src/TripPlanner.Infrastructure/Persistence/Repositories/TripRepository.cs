using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Trips;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Repositories;

public class TripRepository : Repository<Trip>, ITripRepository
{
    public TripRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<List<TripSummaryRow>> GetSummaryRowsForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await Set
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(TripMappings.ToSummaryRowExpression)
            .ToListAsync(cancellationToken);

    public async Task<Trip?> GetDetailsAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
        await Set
            .AsNoTracking()
            .Include(t => t.Days).ThenInclude(d => d.Items).ThenInclude(i => i.Destination)
            .Include(t => t.Items).ThenInclude(i => i.Destination)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, cancellationToken);

    public async Task<Trip?> GetTrackedWithFullGraphAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
        await Set
            .Include(t => t.Days).ThenInclude(d => d.Items)
            .Include(t => t.Items).ThenInclude(i => i.Destination)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, cancellationToken);

    public async Task<bool> DayBelongsToTripAsync(Guid itineraryDayId, Guid tripId, CancellationToken cancellationToken = default) =>
        await Context.Set<ItineraryDay>()
            .AnyAsync(d => d.Id == itineraryDayId && d.TripId == tripId, cancellationToken);

    public async Task<ItineraryItem?> GetOwnedItemAsync(Guid tripId, Guid itemId, Guid userId, CancellationToken cancellationToken = default) =>
        await Context.Set<ItineraryItem>()
            .FirstOrDefaultAsync(i => i.Id == itemId && i.TripId == tripId && i.Trip!.UserId == userId, cancellationToken);
}
