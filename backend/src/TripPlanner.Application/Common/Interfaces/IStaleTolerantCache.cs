using TripPlanner.Application.Common.Caching;

namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Stores a value together with the moment it was fetched, and hands it back
/// however old it is — deciding whether that age is acceptable is the caller's
/// job (see <c>DestinationService</c>'s per-endpoint TTLs and its
/// stale-better-than-down fallback, spec §11.2).
///
/// This port exists to split policy from mechanism. The POLICY — which TTL
/// applies, and that a provider outage is answered with a stale entry rather
/// than a 500 — is a business rule and stays in Application. The MECHANISM —
/// serialization, byte arrays, expiry options, how long entries are retained —
/// is technology, and belongs to the implementation. Before the split,
/// <c>DestinationService</c> carried both, which is why the Application layer
/// referenced <c>System.Text.Json</c> and <c>IDistributedCache</c> at all.
///
/// Implementations must degrade rather than throw on a READ: an unreadable or
/// no-longer-matching entry is reported as a miss (<c>null</c>), because entries
/// outlive DTO reshapes. A WRITE failure is not swallowed — that one is a bug in
/// our own contract, and hiding it once made a provider outage the suspect.
/// </summary>
public interface IStaleTolerantCache
{
    /// <summary>The stored entry, or <c>null</c> if absent or no longer readable as <typeparamref name="T"/>.</summary>
    Task<CacheEnvelope<T>?> TryGetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="envelope"/>, retained well past any caller's TTL.</summary>
    Task SetAsync<T>(string key, CacheEnvelope<T> envelope, CancellationToken cancellationToken = default);
}
