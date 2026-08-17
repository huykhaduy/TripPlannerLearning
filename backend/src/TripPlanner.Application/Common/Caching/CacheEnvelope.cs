namespace TripPlanner.Application.Common.Caching;

/// <summary>
/// A cached value plus WHEN it was fetched.
///
/// Freshness is checked against a TTL by hand (instead of letting the cache
/// backend evict) precisely so an expired entry is still readable as a stale
/// fallback during a provider outage — spec §11.2's "stale-better-than-down".
/// That is why the timestamp travels with the value instead of being the
/// backend's business.
///
/// Was a private nested record in <c>DestinationService</c>; it moved out with
/// the serialization mechanism, unchanged, so the two sides of
/// <see cref="Interfaces.IStaleTolerantCache"/> can name the same shape.
/// </summary>
public sealed record CacheEnvelope<T>(T Value, DateTimeOffset FetchedAt);
