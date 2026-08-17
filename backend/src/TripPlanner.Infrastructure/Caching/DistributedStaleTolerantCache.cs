using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using TripPlanner.Application.Common.Caching;
using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.Infrastructure.Caching;

/// <summary>
/// <see cref="IStaleTolerantCache"/> over whichever <see cref="IDistributedCache"/>
/// backend is configured — JSON in, JSON out, with a retention window.
///
/// Pairs with <see cref="ResilientDistributedCache"/> rather than duplicating it:
/// that decorator turns a BACKEND failure (Redis down, timeout) into a miss before
/// it reaches here, so the only failure this class has to reason about is a
/// payload it cannot read.
/// </summary>
internal sealed class DistributedStaleTolerantCache(
    IDistributedCache cache,
    ILogger<DistributedStaleTolerantCache> logger) : IStaleTolerantCache
{
    /// <summary>
    /// Entries stay resident well past any caller's TTL so a provider outage can be
    /// answered with stale data (spec §11.2); the window bounds memory growth.
    /// </summary>
    private static readonly TimeSpan CacheRetention = TimeSpan.FromDays(7);

    public async Task<CacheEnvelope<T>?> TryGetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var bytes = await cache.GetAsync(key, cancellationToken);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CacheEnvelope<T>>(bytes);
        }
        catch (JsonException ex)
        {
            // Treated as a miss, but NOT silently: entries live for CacheRetention,
            // so one reshaped DTO invalidates every cached entry of that type for a
            // week. Without this line that looks exactly like "the cache mysteriously
            // stopped working".
            logger.LogWarning(ex, "Discarding a cache entry that no longer matches {Type}.", typeof(T).Name);
            return null;
        }
    }

    /// <summary>
    /// Deliberately does NOT mirror the read side's tolerance: a serialize failure
    /// here is a bug in our own contract, and swallowing it would let it be mistaken
    /// for a provider outage and answered with stale data.
    /// </summary>
    public async Task SetAsync<T>(string key, CacheEnvelope<T> envelope, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);

        await cache.SetAsync(
            key, bytes,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheRetention },
            cancellationToken);
    }
}
