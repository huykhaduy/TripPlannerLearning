using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Persistence for the Destination cache (rows mirror external-provider data,
/// keyed by a unique index on ProviderId).
/// </summary>
public interface IDestinationRepository
{
    /// <summary>Tracked fetch — used before a possible add.</summary>
    Task<Destination?> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default);

    /// <summary>Read-only fetch — used by DestinationService's cache fallback, which never mutates the row.</summary>
    Task<Destination?> GetByProviderIdReadOnlyAsync(string providerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the destination and persists immediately. On a ProviderId conflict
    /// the failed entity is detached and <see cref="Exceptions.ConcurrencyException"/>
    /// is rethrown, so the caller can re-fetch the winning row.
    /// </summary>
    Task AddAsync(Destination destination, CancellationToken cancellationToken = default);
}
