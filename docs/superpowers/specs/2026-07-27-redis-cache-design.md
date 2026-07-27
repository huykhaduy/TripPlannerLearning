# Redis Cache — Design

**Date:** 2026-07-27
**Scope:** Replace `DestinationService`'s `IMemoryCache`-backed cache-aside layer
(browse-path caching for location search, attractions, and destination
details — Features 1 & 2) with an `IDistributedCache` abstraction, backed by
either the built-in in-process implementation (default) or Redis (opt-in),
mirroring the existing optional-Postgres pattern. Purpose is educational —
the student wants to practice wiring up Redis as a real technology, not
solving a specific production problem. Backend only, no frontend changes.

## Current state (context)

`DestinationService` ([DestinationService.cs](../../../backend/src/TripPlanner.Application/Features/Destinations/DestinationService.cs))
injects `IMemoryCache _cache` and implements cache-aside manually via a
`CacheEnvelope<T> { T Value, DateTimeOffset FetchedAt }` record, so freshness
is checked by hand against a TTL (`GetUtcNow() - FetchedAt < ttl`) rather than
relying on the cache's own eviction — this is what lets an *expired* entry
still be read as a stale fallback during a provider outage
("stale-better-than-down", spec §11.2). Entries are retained for
`CacheRetention = 7 days` regardless of their (shorter) TTL, bounding memory
growth while keeping a stale-fallback window.

There are three cache-aside call sites, all using `_cache.TryGetValue`/`_cache.Set`
directly (confirmed by grepping every `_cache.` reference — no other usages
exist, e.g. no `.Remove`):

1. **`GetCachedAsync<T>`** (`DestinationService.cs:93-111`) — the generic helper,
   used by `SearchLocationsAsync` (`LocationsTtl` = 24h) and `GetAttractionsAsync`
   (`AttractionsTtl` = 6h). Catches `HttpRequestException` around the provider
   call and falls back to the stale value if one exists.
2. **`GetAttractionImagesAsync`** (`DestinationService.cs:221-253`) — bespoke
   inline logic (not routed through the generic helper) because its fallback
   chain has three tiers: fresh cache → live Serper search → the provider's own
   image → stale cache. `ImageTtl` = 24h.
3. **`GetCachedProviderDetailsAsync`** (`DestinationService.cs:286-311`) — also
   bespoke: a `null` provider answer is deliberately **not** cached (checked by
   hand) so a transient "not found" doesn't stick. `DetailsTtl` = 24h.

`IMemoryCache` is registered via `services.AddMemoryCache()` in
[Infrastructure/DependencyInjection.cs:36](../../../backend/src/TripPlanner.Infrastructure/DependencyInjection.cs#L36).
`DestinationServiceTests.cs:85` constructs a real `new MemoryCache(new MemoryCacheOptions())`
per test (no mock — "real implementations where cheap" per this repo's testing
convention).

The existing optional-Postgres pattern this design mirrors:
`Database:Provider` config key (`"Sqlite"` default / `"Postgres"`), read in
`AddPersistence` ([DependencyInjection.cs:62-77](../../../backend/src/TripPlanner.Infrastructure/DependencyInjection.cs#L62-L77)),
with an optional `docker-compose.yml` service and a README section for
switching.

## Target architecture

### Config switch

New `Cache:Provider` config key: `"Memory"` (default) or `"Redis"`, read in a
new `AddCaching(services, configuration)` method in
`Infrastructure/DependencyInjection.cs`, called from `AddInfrastructure`
alongside `AddPersistence`:

```csharp
private static void AddCaching(IServiceCollection services, IConfiguration configuration)
{
    var provider = configuration["Cache:Provider"] ?? "Memory";

    if (provider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
    {
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis");
        });
    }
    else
    {
        services.AddDistributedMemoryCache();
    }
}
```

This replaces the existing `services.AddMemoryCache();` line. Both branches
register `IDistributedCache` — `AddDistributedMemoryCache()` is ASP.NET Core's
built-in in-process implementation of that same interface, so the *default*
behavior (no `.env`/config changes at all) is unchanged from today: an
in-process, non-persistent cache, just reached through `IDistributedCache`
instead of `IMemoryCache`.

New NuGet package: `Microsoft.Extensions.Caching.StackExchangeRedis`, added to
`TripPlanner.Infrastructure.csproj` (resolved via `dotnet add package` at
implementation time rather than hand-picking a version here).

### Local dev setup

`docker-compose.yml` gets a new optional `redis` service, same shape as the
existing `postgres` one:

```yaml
redis:
  image: redis:7
  container_name: tripplanner-redis
  ports:
    - "6379:6379"
  healthcheck:
    test: ["CMD", "redis-cli", "ping"]
    interval: 10s
    timeout: 5s
    retries: 5
```

`.env.example`/`.env` gets `ConnectionStrings__Redis=localhost:6379` (following
this repo's established convention of moving environment-specific connection
info out of `appsettings.json` and into `.env`). `README.md` gets a
"Switching to Redis (optional)" section mirroring the existing PostgreSQL one:
`docker compose up -d`, set `Cache:Provider` to `Redis` in
`appsettings.Development.json`, done — no migration step needed since caching
has no schema.

## Code changes in `DestinationService.cs`

`IDistributedCache` stores `byte[]?`, not live CLR objects by reference, so
`CacheEnvelope<T>` needs to round-trip through JSON (`System.Text.Json`,
already imported in this file). Rather than duplicate serialization and the
new outage handling three times across the three call sites above, extract
two private helpers that all three now go through:

```csharp
private async Task<CacheEnvelope<T>?> TryGetCachedEnvelopeAsync<T>(string key, CancellationToken cancellationToken)
{
    try
    {
        var bytes = await _cache.GetAsync(key, cancellationToken);
        return bytes is null ? null : JsonSerializer.Deserialize<CacheEnvelope<T>>(bytes);
    }
    catch (Exception ex) when (IsCacheUnavailable(ex))
    {
        return null; // Redis down/unreachable — treat exactly like a cache miss.
    }
}

private async Task SetCachedEnvelopeAsync<T>(string key, CacheEnvelope<T> envelope, CancellationToken cancellationToken)
{
    try
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        await _cache.SetAsync(
            key, bytes,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheRetention },
            cancellationToken);
    }
    catch (Exception ex) when (IsCacheUnavailable(ex))
    {
        // Redis down/unreachable — the freshly-fetched value is still returned
        // to the caller by whichever call site invoked this; just skip caching it.
    }
}

private static bool IsCacheUnavailable(Exception ex) =>
    ex is RedisConnectionException or RedisTimeoutException or TimeoutException;
```

This is a pure internal consolidation — `IsCacheUnavailable` is the *new*
graceful-degradation behavior (per your "treat it like a cache miss" answer),
symmetric with the existing `catch (HttpRequestException) when (stale is not
null)` pattern already in `GetCachedAsync<T>` for provider outages. All three
call sites replace their `_cache.TryGetValue(...)`/`_cache.Set(...)` lines with
calls to these two helpers; none of their surrounding fallback logic (the
three-tier image chain, the null-not-cached rule for details) changes.

`Microsoft.Extensions.Caching.StackExchangeRedis`'s exceptions surface from
the underlying `StackExchange.Redis` client — `RedisConnectionException` and
`RedisTimeoutException` live in the `StackExchange.Redis` namespace, which
`TripPlanner.Application` would need to reference transitively for this catch
clause. **[Design decision]** this is acceptable: it's the same shape of
dependency `IDistributedCache` itself already is (a framework/vendor
abstraction Application depends on directly, same as `IMemoryCache` today),
not a leak of Infrastructure-specific *implementation* details — Application
still never touches `ConnectionMultiplexer` or connection strings.

## Testing

`DestinationServiceTests.cs:85` swaps its real `new MemoryCache(new
MemoryCacheOptions())` for a real `new MemoryDistributedCache(Options.Create(new
MemoryDistributedCacheOptions()))` — still a real, cheap, in-process
implementation (no Redis server needed for `dotnet test`), same testing
philosophy, one-line change. No new tests are strictly required for the
`IsCacheUnavailable` catch path itself, since `MemoryDistributedCache` never
throws those exception types — this path is only exercised against a real
Redis provider, the same way this repo already accepts that some
concurrency/outage paths are "reasoned about, not tested" (see `CLAUDE.md`'s
note on the `DbUpdateException` retry path being untested by the EF InMemory
provider).

## Edge cases

- **Redis configured but unreachable at startup** (e.g. `Cache:Provider=Redis`
  but the container isn't running yet): `AddStackExchangeRedisCache` doesn't
  connect eagerly, so the app still starts; the first cache operation throws,
  which `IsCacheUnavailable` catches — search/attractions/details still work,
  just always-fresh (no caching) until Redis becomes reachable.
- **Existing cached data on a provider switch**: switching `Cache:Provider`
  between Memory and Redis doesn't migrate data — this is expected and fine,
  caches are disposable by nature (worst case, a few extra provider calls
  until entries repopulate).
- **JSON round-trip fidelity**: `CacheEnvelope<T>`'s `DateTimeOffset` and the
  DTOs it wraps (records with only primitive/string/nullable fields) all
  round-trip through `System.Text.Json` with default settings with no custom
  converters needed — verified by inspecting the actual DTO shapes in
  `Destinations/Dtos/`.
