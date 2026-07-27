# Redis Cache Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `DestinationService`'s `IMemoryCache`-backed cache-aside layer with `IDistributedCache`, config-switchable between the built-in in-process implementation (default, zero setup) and Redis (opt-in), mirroring the existing optional-Postgres pattern.

**Architecture:** `DestinationService` depends only on `Microsoft.Extensions.Caching.Distributed.IDistributedCache` (a framework abstraction, same relationship it already has with `IMemoryCache` today). `Infrastructure/DependencyInjection.cs` picks the concrete backend at startup from a new `Cache:Provider` config key (`"Memory"` default / `"Redis"`), exactly like `Database:Provider` already picks between SQLite/Postgres.

**Tech Stack:** ASP.NET Core `Microsoft.Extensions.Caching.Abstractions`/`.Memory` (built-in `IDistributedCache` + its in-process implementation), `Microsoft.Extensions.Caching.StackExchangeRedis` (Redis-backed `IDistributedCache` implementation), `StackExchange.Redis` (for the two exception types used in outage handling), `System.Text.Json` (already in use).

## Global Constraints

- Spec: [docs/superpowers/specs/2026-07-27-redis-cache-design.md](../specs/2026-07-27-redis-cache-design.md).
- Default behavior (no `.env`/config changes at all) must be unchanged: an in-process, non-persistent cache — no Docker required to run the app.
- On a Redis connectivity failure at runtime, `DestinationService` must degrade gracefully (treat it exactly like a cache miss), never throw a 500 to the caller — per the approved design's "degrade gracefully" answer.
- Never hand-pick a NuGet version for a package this repo hasn't already pinned somewhere; let `dotnet add package` resolve it and record whatever version lands in the `.csproj`.
- `TripPlanner.Application` must not take a package dependency on Redis *server* connection concerns (`ConnectionMultiplexer`, connection strings) — only on `IDistributedCache` (abstraction) and the two `StackExchange.Redis` exception types needed for the outage catch clause, per the design's explicit "[Design decision]" on this point.

---

## Task 1: Swap the cache backend from `IMemoryCache` to a config-switchable `IDistributedCache`

**Files:**
- Modify: `backend/src/TripPlanner.Application/TripPlanner.Application.csproj`
- Modify: `backend/src/TripPlanner.Application/Features/Destinations/DestinationService.cs`
- Modify: `backend/src/TripPlanner.Infrastructure/TripPlanner.Infrastructure.csproj`
- Modify: `backend/src/TripPlanner.Infrastructure/DependencyInjection.cs`
- Modify: `backend/tests/TripPlanner.Application.Tests/Destinations/DestinationServiceTests.cs`

**Interfaces:**
- Produces: `DestinationService`'s constructor 4th parameter changes from `IMemoryCache cache` to `IDistributedCache cache` (same position). Two new private helpers: `Task<CacheEnvelope<T>?> TryGetCachedEnvelopeAsync<T>(string key, CancellationToken cancellationToken)` and `Task SetCachedEnvelopeAsync<T>(string key, CacheEnvelope<T> envelope, CancellationToken cancellationToken)`. `GetCachedAsync<T>` gains a trailing `CancellationToken cancellationToken` parameter (was `GetCachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> fetchAsync)`, becomes `GetCachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> fetchAsync, CancellationToken cancellationToken)`). `Infrastructure.DependencyInjection` gains a private `AddCaching(IServiceCollection, IConfiguration)` method, called from `AddInfrastructure` in place of the old `services.AddMemoryCache();` line.
- Consumes: nothing from other tasks (this is the first task).

- [ ] **Step 1: Update `TripPlanner.Application.csproj` — drop `Microsoft.Extensions.Caching.Memory`, add the abstraction + the Redis exception-type package**

Open `backend/src/TripPlanner.Application/TripPlanner.Application.csproj`. Replace:

```xml
  <ItemGroup>
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="10.0.9" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.9" />
  </ItemGroup>
```

with:

```xml
  <ItemGroup>
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
    <PackageReference Include="Microsoft.Extensions.Caching.Abstractions" Version="10.0.9" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.9" />
  </ItemGroup>
```

Then, from `backend/src/TripPlanner.Application/`, run:

```bash
dotnet add package StackExchange.Redis
```

This adds a `<PackageReference Include="StackExchange.Redis" Version="..." />` line
to the `ItemGroup` above with whatever version NuGet resolves — leave it as
added, don't hand-edit the version.

- [ ] **Step 2: Update `DestinationServiceTests.cs` to construct a real `MemoryDistributedCache` — this will fail to compile until Step 4**

Open `backend/tests/TripPlanner.Application.Tests/Destinations/DestinationServiceTests.cs`.
Replace the top `using` block:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
```

with:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Moq;
```

Then replace the `CreateSut` factory (currently around line 79-87):

```csharp
    private static DestinationService CreateSut(
        ApplicationDbContext db,
        Mock<IDestinationProvider> provider,
        FakeClock? clock = null,
        Mock<IImageSearchProvider>? imageSearch = null) =>
        new(new DestinationRepository(db), provider.Object, (imageSearch ?? NoOpImageSearch()).Object,
            new MemoryCache(new MemoryCacheOptions()), clock ?? new FakeClock(),
            new SearchLocationsRequestValidator(), new GetAttractionsRequestValidator(),
            new GetDestinationDetailsRequestValidator());
```

with:

```csharp
    private static DestinationService CreateSut(
        ApplicationDbContext db,
        Mock<IDestinationProvider> provider,
        FakeClock? clock = null,
        Mock<IImageSearchProvider>? imageSearch = null) =>
        new(new DestinationRepository(db), provider.Object, (imageSearch ?? NoOpImageSearch()).Object,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), clock ?? new FakeClock(),
            new SearchLocationsRequestValidator(), new GetAttractionsRequestValidator(),
            new GetDestinationDetailsRequestValidator());
```

(`MemoryDistributedCache`/`MemoryDistributedCacheOptions` live in the
`Microsoft.Extensions.Caching.Distributed` namespace but ship inside the
`Microsoft.Extensions.Caching.Memory` package — the test project gets that
package transitively via its existing `ProjectReference` to
`TripPlanner.Infrastructure.csproj`, which Step 5 below adds it to. `Options.Create`
comes from `Microsoft.Extensions.Options`, likewise transitive via Infrastructure.)

- [ ] **Step 3: Run the build and confirm it fails with the expected error**

From `backend/`, run:

```bash
dotnet build
```

Expected: **build FAILS** with a `CS1503`/`CS7036`-style error on
`DestinationServiceTests.cs`'s `CreateSut`, something like "cannot convert
from 'Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache' to
'Microsoft.Extensions.Caching.Memory.IMemoryCache'" — this confirms the test
double change is real and `DestinationService` hasn't been updated yet.

- [ ] **Step 4: Rewrite `DestinationService.cs`'s cache dependency, helpers, and all three cache-aside call sites**

Open `backend/src/TripPlanner.Application/Features/Destinations/DestinationService.cs`.

Replace the top `using` block:

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Caching.Memory;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Destinations.Dtos;
using TripPlanner.Domain.Entities;
```

with:

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Destinations.Dtos;
using TripPlanner.Domain.Entities;
```

Replace the field declaration:

```csharp
    private readonly IMemoryCache _cache;
```

with:

```csharp
    private readonly IDistributedCache _cache;
```

Replace the constructor:

```csharp
    public DestinationService(
        IDestinationRepository destinations,
        IDestinationProvider provider,
        IImageSearchProvider imageSearch,
        IMemoryCache cache,
        TimeProvider clock,
        IValidator<SearchLocationsRequest> searchValidator,
        IValidator<GetAttractionsRequest> attractionsValidator,
        IValidator<GetDestinationDetailsRequest> detailsValidator)
    {
        _destinations = destinations;
        _provider = provider;
        _imageSearch = imageSearch;
        _cache = cache;
        _clock = clock;
        _searchValidator = searchValidator;
        _attractionsValidator = attractionsValidator;
        _detailsValidator = detailsValidator;
    }
```

with:

```csharp
    public DestinationService(
        IDestinationRepository destinations,
        IDestinationProvider provider,
        IImageSearchProvider imageSearch,
        IDistributedCache cache,
        TimeProvider clock,
        IValidator<SearchLocationsRequest> searchValidator,
        IValidator<GetAttractionsRequest> attractionsValidator,
        IValidator<GetDestinationDetailsRequest> detailsValidator)
    {
        _destinations = destinations;
        _provider = provider;
        _imageSearch = imageSearch;
        _cache = cache;
        _clock = clock;
        _searchValidator = searchValidator;
        _attractionsValidator = attractionsValidator;
        _detailsValidator = detailsValidator;
    }
```

Replace `GetCachedAsync<T>` and the doc comment/record right above it:

```csharp
    /// <summary>
    /// A cached value plus WHEN it was fetched. Freshness is checked against
    /// the TTL by hand (instead of letting the cache evict) precisely so an
    /// expired entry is still readable as a stale fallback during an outage.
    /// </summary>
    private sealed record CacheEnvelope<T>(T Value, DateTimeOffset FetchedAt);

    /// <summary>
    /// Cache-aside over the provider (NFR1/NFR2): fresh hit → no provider
    /// call; miss/expired → fetch and re-cache; provider down but a stale
    /// entry exists → serve it rather than fail (spec §11.2).
    /// </summary>
    private async Task<T> GetCachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> fetchAsync)
    {
        var stale = _cache.TryGetValue(key, out CacheEnvelope<T>? envelope) ? envelope : null;
        if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < ttl)
        {
            return stale.Value;
        }

        try
        {
            var value = await fetchAsync();
            _cache.Set(key, new CacheEnvelope<T>(value, _clock.GetUtcNow()), CacheRetention);
            return value;
        }
        catch (HttpRequestException) when (stale is not null)
        {
            return stale.Value; // stale-better-than-down
        }
    }
```

with:

```csharp
    /// <summary>
    /// A cached value plus WHEN it was fetched. Freshness is checked against
    /// the TTL by hand (instead of letting the cache evict) precisely so an
    /// expired entry is still readable as a stale fallback during an outage.
    /// </summary>
    private sealed record CacheEnvelope<T>(T Value, DateTimeOffset FetchedAt);

    /// <summary>
    /// Reads and JSON-deserializes a <see cref="CacheEnvelope{T}"/> from
    /// <see cref="IDistributedCache"/>. A connectivity failure (Redis down or
    /// unreachable) is treated exactly like a cache miss — never surfaced to
    /// the caller — per the "degrade gracefully" design decision.
    /// </summary>
    private async Task<CacheEnvelope<T>?> TryGetCachedEnvelopeAsync<T>(string key, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _cache.GetAsync(key, cancellationToken);
            return bytes is null ? null : JsonSerializer.Deserialize<CacheEnvelope<T>>(bytes);
        }
        catch (Exception ex) when (IsCacheUnavailable(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// JSON-serializes and writes a <see cref="CacheEnvelope{T}"/> to
    /// <see cref="IDistributedCache"/>, retained for <see cref="CacheRetention"/>.
    /// A connectivity failure is swallowed — the freshly-fetched value the
    /// caller already has is still returned; this round just isn't cached.
    /// </summary>
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
        }
    }

    private static bool IsCacheUnavailable(Exception ex) =>
        ex is RedisConnectionException or RedisTimeoutException or TimeoutException;

    /// <summary>
    /// Cache-aside over the provider (NFR1/NFR2): fresh hit → no provider
    /// call; miss/expired → fetch and re-cache; provider down but a stale
    /// entry exists → serve it rather than fail (spec §11.2).
    /// </summary>
    private async Task<T> GetCachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> fetchAsync, CancellationToken cancellationToken)
    {
        var stale = await TryGetCachedEnvelopeAsync<T>(key, cancellationToken);
        if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < ttl)
        {
            return stale.Value;
        }

        try
        {
            var value = await fetchAsync();
            await SetCachedEnvelopeAsync(key, new CacheEnvelope<T>(value, _clock.GetUtcNow()), cancellationToken);
            return value;
        }
        catch (HttpRequestException) when (stale is not null)
        {
            return stale.Value; // stale-better-than-down
        }
    }
```

Replace the `SearchLocationsAsync` call to `GetCachedAsync` — find:

```csharp
        var loweredQuery = trimmedQuery.ToLowerInvariant();
        return await GetCachedAsync<IReadOnlyList<LocationSuggestionDto>>(
            $"loc:{loweredQuery}", LocationsTtl, async () =>
            {
                var suggestions = await _provider.SearchLocationsAsync(trimmedQuery, cancellationToken);

                // Geoapify can return the same city under several place ids — dedupe on
                // what the user actually sees (name + country), rank exact and prefix
                // matches above substring hits (F1/US2 business rule), then cap at 5.
                return suggestions
                    .DistinctBy(s => (s.Name.ToLowerInvariant(), s.Country?.ToLowerInvariant()))
                    .OrderBy(s => RelevanceRank(s.Name, loweredQuery))
                    .Take(MaxLocationResults)
                    .ToList();
            });
```

replace with (only the trailing argument list changes — adds `, cancellationToken` before the closing paren):

```csharp
        var loweredQuery = trimmedQuery.ToLowerInvariant();
        return await GetCachedAsync<IReadOnlyList<LocationSuggestionDto>>(
            $"loc:{loweredQuery}", LocationsTtl, async () =>
            {
                var suggestions = await _provider.SearchLocationsAsync(trimmedQuery, cancellationToken);

                // Geoapify can return the same city under several place ids — dedupe on
                // what the user actually sees (name + country), rank exact and prefix
                // matches above substring hits (F1/US2 business rule), then cap at 5.
                return suggestions
                    .DistinctBy(s => (s.Name.ToLowerInvariant(), s.Country?.ToLowerInvariant()))
                    .OrderBy(s => RelevanceRank(s.Name, loweredQuery))
                    .Take(MaxLocationResults)
                    .ToList();
            }, cancellationToken);
```

Replace the `GetAttractionsAsync` call to `GetCachedAsync` — find:

```csharp
        return await GetCachedAsync<IReadOnlyList<DestinationSummaryDto>>(
            key, AttractionsTtl, async () =>
            {
                var attractions = await _provider.GetAttractionsAsync(latitude, longitude, radiusKm, cancellationToken);

                // "Recommended" default sort (spec §11.2): rating descending, unrated
                // last — which with Geoapify (no ratings) degrades to provider order.
                // Ranked/capped BEFORE image enrichment so we only spend extra provider
                // calls on the ≤20 results we actually return.
                var ranked = attractions
                    .OrderBy(a => a.Rating is null)
                    .ThenByDescending(a => a.Rating)
                    .Take(MaxAttractionResults)
                    .ToList();

                return await EnrichWithImagesAsync(ranked, cancellationToken);
            });
```

replace with:

```csharp
        return await GetCachedAsync<IReadOnlyList<DestinationSummaryDto>>(
            key, AttractionsTtl, async () =>
            {
                var attractions = await _provider.GetAttractionsAsync(latitude, longitude, radiusKm, cancellationToken);

                // "Recommended" default sort (spec §11.2): rating descending, unrated
                // last — which with Geoapify (no ratings) degrades to provider order.
                // Ranked/capped BEFORE image enrichment so we only spend extra provider
                // calls on the ≤20 results we actually return.
                var ranked = attractions
                    .OrderBy(a => a.Rating is null)
                    .ThenByDescending(a => a.Rating)
                    .Take(MaxAttractionResults)
                    .ToList();

                return await EnrichWithImagesAsync(ranked, cancellationToken);
            }, cancellationToken);
```

Replace `GetAttractionImagesAsync`:

```csharp
    private async Task<IReadOnlyList<string>> GetAttractionImagesAsync(string providerId, string name, string? providerImage, CancellationToken cancellationToken)
    {
        var key = $"imgs:{providerId}";
        var stale = _cache.TryGetValue(key, out CacheEnvelope<IReadOnlyList<string>>? envelope) ? envelope : null;
        if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < ImageTtl)
        {
            return stale.Value;
        }

        IReadOnlyList<string> images = [];
        try
        {
            images = await _imageSearch.SearchImagesAsync(name, DetailsPhotoCount, cancellationToken);
        }
        catch (Exception ex) when (IsTransientExternalFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            // Serper down, timed out, or returned something unparseable — fall
            // through to the next source rather than failing the whole list/page.
        }

        if (images.Count == 0 && providerImage is not null)
        {
            images = [providerImage];
        }

        if (images.Count == 0 && stale is not null)
        {
            images = stale.Value; // nothing resolved this round — prefer a stale gallery over none at all
        }

        _cache.Set(key, new CacheEnvelope<IReadOnlyList<string>>(images, _clock.GetUtcNow()), CacheRetention);
        return images;
    }
```

with:

```csharp
    private async Task<IReadOnlyList<string>> GetAttractionImagesAsync(string providerId, string name, string? providerImage, CancellationToken cancellationToken)
    {
        var key = $"imgs:{providerId}";
        var stale = await TryGetCachedEnvelopeAsync<IReadOnlyList<string>>(key, cancellationToken);
        if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < ImageTtl)
        {
            return stale.Value;
        }

        IReadOnlyList<string> images = [];
        try
        {
            images = await _imageSearch.SearchImagesAsync(name, DetailsPhotoCount, cancellationToken);
        }
        catch (Exception ex) when (IsTransientExternalFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            // Serper down, timed out, or returned something unparseable — fall
            // through to the next source rather than failing the whole list/page.
        }

        if (images.Count == 0 && providerImage is not null)
        {
            images = [providerImage];
        }

        if (images.Count == 0 && stale is not null)
        {
            images = stale.Value; // nothing resolved this round — prefer a stale gallery over none at all
        }

        await SetCachedEnvelopeAsync(key, new CacheEnvelope<IReadOnlyList<string>>(images, _clock.GetUtcNow()), cancellationToken);
        return images;
    }
```

Replace `GetCachedProviderDetailsAsync`:

```csharp
    private async Task<DestinationDetailsDto?> GetCachedProviderDetailsAsync(string providerId, CancellationToken cancellationToken)
    {
        var key = $"details:{providerId}";
        var stale = _cache.TryGetValue(key, out CacheEnvelope<DestinationDetailsDto>? envelope) ? envelope : null;
        if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < DetailsTtl)
        {
            return stale.Value;
        }

        DestinationDetailsDto? details;
        try
        {
            details = await _provider.GetDestinationDetailsAsync(providerId, cancellationToken);
        }
        catch (Exception ex) when (IsTransientExternalFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            return stale?.Value; // stale-better-than-down (spec §11.2), else null
        }

        if (details is not null)
        {
            _cache.Set(key, new CacheEnvelope<DestinationDetailsDto>(details, _clock.GetUtcNow()), CacheRetention);
        }

        return details;
    }
```

with:

```csharp
    private async Task<DestinationDetailsDto?> GetCachedProviderDetailsAsync(string providerId, CancellationToken cancellationToken)
    {
        var key = $"details:{providerId}";
        var stale = await TryGetCachedEnvelopeAsync<DestinationDetailsDto>(key, cancellationToken);
        if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < DetailsTtl)
        {
            return stale.Value;
        }

        DestinationDetailsDto? details;
        try
        {
            details = await _provider.GetDestinationDetailsAsync(providerId, cancellationToken);
        }
        catch (Exception ex) when (IsTransientExternalFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            return stale?.Value; // stale-better-than-down (spec §11.2), else null
        }

        if (details is not null)
        {
            await SetCachedEnvelopeAsync(key, new CacheEnvelope<DestinationDetailsDto>(details, _clock.GetUtcNow()), cancellationToken);
        }

        return details;
    }
```

- [ ] **Step 5: Add packages to `TripPlanner.Infrastructure.csproj` and switch the DI registration**

Open `backend/src/TripPlanner.Infrastructure/TripPlanner.Infrastructure.csproj`. Replace:

```xml
    <PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
  </ItemGroup>
```

with:

```xml
    <PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="10.0.9" />
  </ItemGroup>
```

Then, from `backend/src/TripPlanner.Infrastructure/`, run:

```bash
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis
```

Leave whatever version NuGet resolves as-is in the `.csproj`.

Now open `backend/src/TripPlanner.Infrastructure/DependencyInjection.cs`. Replace:

```csharp
        // In-memory cache for browse-path provider results (spec §11.2 NFR1/NFR2).
        services.AddMemoryCache();
```

with:

```csharp
        // Cache backend for browse-path provider results (spec §11.2 NFR1/NFR2),
        // switchable from configuration: "Cache:Provider" = "Memory" (default) or
        // "Redis". Both branches register IDistributedCache — DestinationService
        // depends only on that abstraction, never on StackExchange.Redis directly.
        AddCaching(services, configuration);
```

Then add this new private method right after the existing `AddPersistence` method (at the bottom of the class, before the closing brace):

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

- [ ] **Step 6: Build and confirm it now succeeds**

From `backend/`, run:

```bash
dotnet build
```

Expected: `Build succeeded.` (0 errors — pre-existing `NU1903` vulnerability
warnings on unrelated packages are fine and expected).

- [ ] **Step 7: Run the full test suite and confirm no regressions**

From `backend/`, run:

```bash
dotnet test
```

Expected: all tests pass, same total count as before this task (78 as of the
last recorded run) — this proves the cache-backend swap preserved every
existing behavior (fresh-hit, TTL-expiry refetch, stale-on-provider-outage
fallback, the three-tier image fallback, the null-not-cached details rule).

- [ ] **Step 8: Manual smoke test against the default (Memory) backend**

From `backend/`, run:

```bash
dotnet run --project src/TripPlanner.WebApi
```

In another terminal, run:

```bash
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:5080/api/destinations/locations?query=Paris"
```

Expected: `200` (assumes `Geoapify__ApiKey` is already set in
`backend/src/TripPlanner.WebApi/.env` from earlier setup). Check the running
process's console output for any exception — there should be none. Stop the
`dotnet run` process (Ctrl+C or stop the background task).

- [ ] **Step 9: Commit**

```bash
git add backend/src/TripPlanner.Application/TripPlanner.Application.csproj \
        backend/src/TripPlanner.Application/Features/Destinations/DestinationService.cs \
        backend/src/TripPlanner.Infrastructure/TripPlanner.Infrastructure.csproj \
        backend/src/TripPlanner.Infrastructure/DependencyInjection.cs \
        backend/tests/TripPlanner.Application.Tests/Destinations/DestinationServiceTests.cs
git commit -m "Switch DestinationService cache to IDistributedCache, config-switchable to Redis"
```

---

## Task 2: Add the optional Redis service, connection config, and documentation

**Files:**
- Modify: `docker-compose.yml`
- Modify: `backend/src/TripPlanner.WebApi/.env.example`
- Modify: `backend/src/TripPlanner.WebApi/.env`
- Modify: `README.md`
- Modify: `CLAUDE.md`
- Modify: `TECHNICAL_SPEC.md`

**Interfaces:**
- Consumes: `Cache:Provider` config key and `ConnectionStrings:Redis` connection string, both read by `AddCaching` from Task 1.
- Produces: nothing consumed by later tasks (this is the last task).

- [ ] **Step 1: Add the `redis` service to `docker-compose.yml`**

Open `docker-compose.yml`. Replace:

```yaml
services:
  postgres:
    image: postgres:17
    container_name: tripplanner-postgres
    environment:
      POSTGRES_USER: tripplanner
      POSTGRES_PASSWORD: tripplanner
      POSTGRES_DB: tripplanner
    ports:
      - "5432:5432"
    volumes:
      - tripplanner_pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U tripplanner"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  tripplanner_pgdata:
```

with:

```yaml
services:
  postgres:
    image: postgres:17
    container_name: tripplanner-postgres
    environment:
      POSTGRES_USER: tripplanner
      POSTGRES_PASSWORD: tripplanner
      POSTGRES_DB: tripplanner
    ports:
      - "5432:5432"
    volumes:
      - tripplanner_pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U tripplanner"]
      interval: 10s
      timeout: 5s
      retries: 5

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

volumes:
  tripplanner_pgdata:
```

Also update the file's leading comment block — replace:

```yaml
# Optional PostgreSQL service for students who want to practise running against
# a "real" database instead of the default SQLite file.
#
#   1. docker compose up -d
#   2. In src/TripPlanner.WebApi/appsettings.Development.json set:
#        "Database": { "Provider": "Postgres" }
#      and use the "Postgres" connection string already provided there.
#   3. Re-create migrations for the Postgres provider, then `dotnet ef database update`.
#
# The default template runs on SQLite with NO Docker required.
```

with:

```yaml
# Optional services for students who want to practise running against "real"
# infrastructure instead of the zero-setup defaults (SQLite, in-process cache).
#
# Postgres:
#   1. docker compose up -d
#   2. In src/TripPlanner.WebApi/appsettings.Development.json set:
#        "Database": { "Provider": "Postgres" }
#      and use the "Postgres" connection string already provided there.
#   3. Re-create migrations for the Postgres provider, then `dotnet ef database update`.
#
# Redis:
#   1. docker compose up -d
#   2. In src/TripPlanner.WebApi/appsettings.Development.json set:
#        "Cache": { "Provider": "Redis" }
#      and use the "Redis" connection string already provided in .env.
#
# The default template runs on SQLite + an in-process cache with NO Docker required.
```

- [ ] **Step 2: Add the Redis connection string to `.env.example` and `.env`**

Open `backend/src/TripPlanner.WebApi/.env.example`. Replace:

```
# Only read when Database:Provider is switched to "Postgres" in
# appsettings.Development.json. Matches docker-compose.yml's default creds.
ConnectionStrings__Postgres=Host=localhost;Port=5432;Database=tripplanner;Username=tripplanner;Password=tripplanner
```

with:

```
# Only read when Database:Provider is switched to "Postgres" in
# appsettings.Development.json. Matches docker-compose.yml's default creds.
ConnectionStrings__Postgres=Host=localhost;Port=5432;Database=tripplanner;Username=tripplanner;Password=tripplanner

# Only read when Cache:Provider is switched to "Redis" in
# appsettings.Development.json. Matches docker-compose.yml's default port.
ConnectionStrings__Redis=localhost:6379
```

Open `backend/src/TripPlanner.WebApi/.env` (the real local file — this repo's
convention is to keep it working out of the box for every optional feature,
same as the existing `ConnectionStrings__Postgres` line already there).
Replace:

```
ConnectionStrings__Postgres=Host=localhost;Port=5432;Database=tripplanner;Username=tripplanner;Password=tripplanner
```

with:

```
ConnectionStrings__Postgres=Host=localhost;Port=5432;Database=tripplanner;Username=tripplanner;Password=tripplanner
ConnectionStrings__Redis=localhost:6379
```

- [ ] **Step 3: Add a "Switching to Redis (optional)" section to `README.md`**

Open `README.md`. Find the existing "Switching to PostgreSQL (optional)"
section (ends with the `dotnet ef migrations add InitialCreate` / `dotnet run`
line) and insert a new section immediately after it:

```markdown

## Switching to Redis (optional)

1. `docker compose up -d` (starts Redis on port 6379 — safe to run alongside Postgres).
2. In `backend/src/TripPlanner.WebApi/appsettings.Development.json`, add:
   ```json
   { "Cache": { "Provider": "Redis" } }
   ```
3. The `ConnectionStrings__Redis` value lives in `.env` (see `.env.example`) —
   its default already matches `docker-compose.yml`'s port, so no change is
   needed unless you edit the compose file.
4. No migration step needed — caching has no schema. Just restart the API
   (`dotnet run`); destination search/attractions/details now cache through
   Redis instead of the in-process default.
```

- [ ] **Step 4: Add a `Cache:Provider` line to `CLAUDE.md`'s Configuration section**

Open `CLAUDE.md`. Find this line in the `## Configuration` section:

```markdown
- Database defaults to SQLite (`tripplanner.db` in the WebApi folder). Switch to PostgreSQL by setting `"Database": { "Provider": "Postgres" }` in `appsettings.Development.json` and running `docker compose up -d`.
```

and insert this new line immediately after it:

```markdown
- Destination browse-path caching (`DestinationService`) defaults to an in-process `IDistributedCache`. Switch to Redis by setting `"Cache": { "Provider": "Redis" } }` in `appsettings.Development.json` and running `docker compose up -d`; the connection string comes from `.env` (`ConnectionStrings__Redis`), not `appsettings.json`.
```

- [ ] **Step 5: Update `TECHNICAL_SPEC.md`'s B20 rule, dependency table, and cache description**

Open `TECHNICAL_SPEC.md`.

Replace (around line 617):

```markdown
| B20 | Provider/browse results are cached (cache-aside, `IMemoryCache`) with TTLs of 24h (locations, details), 6h (attractions), and stale-while-revalidate fallback on a provider outage | `DestinationService.GetCachedAsync`/`GetCachedProviderDetailsAsync`/`GetAttractionImagesAsync` | — |
```

with:

```markdown
| B20 | Provider/browse results are cached (cache-aside, `IDistributedCache` — in-process by default, Redis when `Cache:Provider` is set) with TTLs of 24h (locations, details), 6h (attractions), and stale-while-revalidate fallback on a provider outage **or a cache backend outage** | `DestinationService.GetCachedAsync`/`GetCachedProviderDetailsAsync`/`GetAttractionImagesAsync` via the shared `TryGetCachedEnvelopeAsync`/`SetCachedEnvelopeAsync` helpers | — |
```

Replace (around lines 136-137):

```markdown
- `TripPlanner.Application` references Domain + `FluentValidation.DependencyInjectionExtensions`
  + `Microsoft.Extensions.Caching.Memory` + `Microsoft.Extensions.DependencyInjection.Abstractions`.
```

with:

```markdown
- `TripPlanner.Application` references Domain + `FluentValidation.DependencyInjectionExtensions`
  + `Microsoft.Extensions.Caching.Abstractions` + `StackExchange.Redis` (only for the
  `RedisConnectionException`/`RedisTimeoutException` types used in `DestinationService`'s
  cache-outage handling — Application never touches `ConnectionMultiplexer` or a
  connection string) + `Microsoft.Extensions.DependencyInjection.Abstractions`.
```

Replace (around line 1044):

```markdown
| Microsoft.Extensions.Caching.Memory | 10.0.9 | **Application** | `IMemoryCache` used by `DestinationService`'s cache-aside layer (§3.2) |
```

with:

```markdown
| Microsoft.Extensions.Caching.Abstractions | 10.0.9 | **Application** | `IDistributedCache` used by `DestinationService`'s cache-aside layer (§3.2) |
| StackExchange.Redis | (resolved by NuGet) | **Application** | `RedisConnectionException`/`RedisTimeoutException` types only, for cache-outage detection — no direct Redis connection code in Application |
| Microsoft.Extensions.Caching.Memory | 10.0.9 | Infrastructure | `AddDistributedMemoryCache()` — the default, in-process `IDistributedCache` implementation |
| Microsoft.Extensions.Caching.StackExchangeRedis | (resolved by NuGet) | Infrastructure | `AddStackExchangeRedisCache()` — the opt-in Redis-backed `IDistributedCache` implementation |
```

Replace (around lines 1082-1085):

```markdown
- **In-process cache**: `IMemoryCache` (`Microsoft.Extensions.Caching.Memory`) backs
  `DestinationService`'s cache-aside layer — not a distributed cache, so it does not
  survive a process restart and is not shared across horizontally-scaled instances.
  [Observed]
```

with:

```markdown
- **Cache**: `IDistributedCache` backs `DestinationService`'s cache-aside layer,
  switchable via `Cache:Provider` — `AddDistributedMemoryCache()` (default, in-process;
  does not survive a restart, not shared across instances) or
  `AddStackExchangeRedisCache()` (optional, `docker-compose.yml`'s `redis` service;
  survives restarts, shareable across instances). A cache-backend connectivity failure
  is caught and treated as a cache miss — search/attractions/details keep working,
  just always-fresh, until the cache is reachable again. [Observed]
```

- [ ] **Step 6: Manual end-to-end verification against real Redis**

From the repo root, run:

```bash
docker compose up -d redis
```

Wait for it to report healthy:

```bash
docker compose ps redis
```

Expected: `STATUS` column shows `healthy` (may take up to ~10s).

Edit `backend/src/TripPlanner.WebApi/appsettings.Development.json` to temporarily add:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Information"
    }
  },
  "Cache": { "Provider": "Redis" }
}
```

Start the backend:

```bash
cd backend && dotnet run --project src/TripPlanner.WebApi
```

In another terminal, call the same endpoint twice:

```bash
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:5080/api/destinations/locations?query=Paris"
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:5080/api/destinations/locations?query=Paris"
```

Expected: both `200`. Confirm the entry actually landed in Redis:

```bash
docker exec tripplanner-redis redis-cli KEYS "loc:*"
```

Expected: at least one key printed (e.g. `loc:paris`).

Now confirm graceful degradation — stop Redis while the backend keeps running:

```bash
docker compose stop redis
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:5080/api/destinations/locations?query=Paris"
```

Expected: still `200` (not 500) — the backend's console log should show no
unhandled exception, confirming `IsCacheUnavailable` caught the connectivity
failure and treated it as a cache miss.

Restart Redis and stop the backend:

```bash
docker compose start redis
```

Stop the `dotnet run` process (Ctrl+C or stop the background task).

**Revert** the temporary `appsettings.Development.json` edit — remove the
`"Cache": { "Provider": "Redis" }` line so the file goes back to just:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
```

(This file is meant to demonstrate the *optional* switch, not permanently
enable Redis for every future `dotnet run` — leaving it set would silently
require Docker for everyone who clones the repo afterward.)

- [ ] **Step 7: Commit**

```bash
git add docker-compose.yml \
        backend/src/TripPlanner.WebApi/.env.example \
        backend/src/TripPlanner.WebApi/.env \
        README.md \
        CLAUDE.md \
        TECHNICAL_SPEC.md
git commit -m "Add optional Redis service and docs for the Cache:Provider switch"
```
