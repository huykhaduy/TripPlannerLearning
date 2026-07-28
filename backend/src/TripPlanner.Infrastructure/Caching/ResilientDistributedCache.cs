using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace TripPlanner.Infrastructure.Caching;

/// <summary>
/// Wraps whichever <see cref="IDistributedCache"/> backend is configured
/// (in-process or Redis) so a connectivity failure — Redis down/unreachable,
/// or a bare timeout — degrades to a cache miss/no-op instead of throwing.
/// This is the only place in the solution allowed to reference
/// <c>StackExchange.Redis</c> exception types: Application only ever sees
/// <see cref="IDistributedCache"/>, and a miss/no-op is indistinguishable
/// from "not cached yet" — exactly the "degrade gracefully" behavior the
/// caching design calls for (spec §11.2), without Application needing to
/// know which backend is configured or how it fails.
/// </summary>
internal sealed class ResilientDistributedCache(IDistributedCache inner) : IDistributedCache
{
    public byte[]? Get(string key) => Guard(() => inner.Get(key), fallback: null);

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
        GuardAsync(() => inner.GetAsync(key, token), fallback: null);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
        Guard(() => { inner.Set(key, value, options); return true; }, fallback: false);

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
        GuardAsync(async () => { await inner.SetAsync(key, value, options, token); return true; }, fallback: false);

    public void Refresh(string key) =>
        Guard(() => { inner.Refresh(key); return true; }, fallback: false);

    public Task RefreshAsync(string key, CancellationToken token = default) =>
        GuardAsync(async () => { await inner.RefreshAsync(key, token); return true; }, fallback: false);

    public void Remove(string key) =>
        Guard(() => { inner.Remove(key); return true; }, fallback: false);

    public Task RemoveAsync(string key, CancellationToken token = default) =>
        GuardAsync(async () => { await inner.RemoveAsync(key, token); return true; }, fallback: false);

    private static T Guard<T>(Func<T> action, T fallback)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (IsCacheUnavailable(ex))
        {
            return fallback;
        }
    }

    private static async Task<T> GuardAsync<T>(Func<Task<T>> action, T fallback)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (IsCacheUnavailable(ex))
        {
            return fallback;
        }
    }

    private static bool IsCacheUnavailable(Exception ex) =>
        ex is RedisConnectionException or RedisTimeoutException or TimeoutException;
}
