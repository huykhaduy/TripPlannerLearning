using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Repositories;

public class DestinationRepository : IDestinationRepository
{
    private readonly ApplicationDbContext _context;

    public DestinationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Destination?> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default) =>
        await _context.Destinations.FirstOrDefaultAsync(d => d.ProviderId == providerId, cancellationToken);

    public async Task<Destination?> GetByProviderIdReadOnlyAsync(string providerId, CancellationToken cancellationToken = default) =>
        await _context.Destinations.AsNoTracking().FirstOrDefaultAsync(d => d.ProviderId == providerId, cancellationToken);

    public async Task AddAsync(Destination destination, CancellationToken cancellationToken = default)
    {
        _context.Destinations.Add(destination);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            // A concurrent request inserted this ProviderId first. Detach our losing
            // copy so the caller can re-fetch the winner on a clean change tracker.
            _context.Entry(destination).State = EntityState.Detached;
            throw;
        }
    }
}
