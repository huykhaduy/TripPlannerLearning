using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Repositories;

public class DestinationRepository : Repository<Destination>, IDestinationRepository
{
    public DestinationRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<Destination?> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(d => d.ProviderId == providerId, cancellationToken);

    public async Task<Destination?> GetByProviderIdReadOnlyAsync(string providerId, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().FirstOrDefaultAsync(d => d.ProviderId == providerId, cancellationToken);
}
