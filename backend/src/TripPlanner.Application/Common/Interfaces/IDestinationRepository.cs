using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Common.Interfaces;

public interface IDestinationRepository : IRepository<Destination>
{
    /// <summary>Tracked fetch — used before a possible Add (see TripService.GetOrCreateDestinationAsync).</summary>
    Task<Destination?> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default);

    /// <summary>Read-only fetch — used by DestinationService's cache fallback, which never mutates the row.</summary>
    Task<Destination?> GetByProviderIdReadOnlyAsync(string providerId, CancellationToken cancellationToken = default);
}
