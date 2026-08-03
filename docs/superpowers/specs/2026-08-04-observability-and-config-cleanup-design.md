# Observability and configuration cleanup

**Date:** 2026-08-04
**Status:** approved (design), not yet implemented

Four independent changes. Each lands on its own and can be skipped without affecting the
others. Ordered by value per unit of churn.

| § | Change | Files touched |
|---|---|---|
| 1 | Log external-service failures; stop the one truly silent swallow | 5 |
| 2 | `Program.cs` reads JWT config through the options pattern | 4 (+ CLAUDE.md) |
| 3 | `BaseEntity.Id` declared `ValueGeneratedNever()` | 6 (+ 3 migration files) |
| 4 | Split `UpdateItineraryItemAsync` | 1 |

§1 files: `SmtpEmailSender`, `SerperImageClient`, `GeoapifyClient`,
`ResilientDistributedCache`, `DependencyInjection` (passes a logger to the cache
decorator, which is constructed by hand), and `TripPlanner.Infrastructure.csproj`
(`Microsoft.Extensions.Logging.Abstractions` was only available transitively via EF Core;
now referenced explicitly, matching how `Options` and `Http` are already listed).
§2 files: `Program.cs`, `CustomWebApplicationFactory`, `DependencyInjection` (Infrastructure,
if the Jwt section registration moves), `CLAUDE.md`.
§3 files: `UserConfiguration`, `TripConfiguration` (holds three configuration classes),
`DestinationConfiguration`, `ITripRepository`, `TripRepository`, `TripService`, plus the
migration, its designer, and the model snapshot.

---

## 1. Log external-service failures

### Problem

`ILogger` appears in **one** of ~40 source files (`ExceptionHandlingMiddleware`). Five
`catch` blocks in the Application layer and two in `ResilientDistributedCache` discard
their exception entirely. Today, if Serper starts failing, SMTP is misconfigured, or Redis
drops, **nothing is recorded anywhere** — images quietly vanish, verification emails
quietly never arrive, and the cache quietly stops caching.

### Rule: adapters log, callers decide

Every adapter catches its own transient failures, logs them at Warning, and **rethrows**.
No adapter swallows. Callers keep whatever fallback policy they already had, unchanged.

This keeps the Application layer free of `ILogger` — **no new constructor dependencies,
and no logging package added to `TripPlanner.Application`.**

> **Revised during implementation.** This section originally had `SmtpEmailSender`
> swallow its failures, on the grounds that no caller acts on them. That was wrong:
> `AuthServiceTests.RegisterAsync_WhenEmailSendingFails_StillCreatesTheAccount` asserts
> the F4/US1 guarantee that registration survives a mail outage. Swallowing in the adapter
> would have moved that guarantee out of the layer where it is tested and into one where
> nothing tests it. The uniform "log and rethrow" rule is also simpler to defend than a
> per-adapter judgement call.

### Changes

**`SmtpEmailSender.SendAsync`** — catch `SmtpException`/`SocketException`, log Warning,
rethrow. `AuthService` keeps its existing catch, which is where the "registration survives
a mail outage" guarantee is tested.

Do **not** put the recipient address in the log message — it is personal data, and logs
are the easiest place to leak it. The subject plus the exception is enough to diagnose an
SMTP outage. The existing early return for unconfigured SMTP (empty `User`/`AppPassword`)
also logs — at Debug, since on a fresh clone it is expected, not a fault.

**`SerperImageClient`** — catch `HttpRequestException`/`TaskCanceledException`/`JsonException`
in `SearchImagesAsync`, log Warning with the query, then **rethrow**.

Must rethrow: `DestinationService` sets `searchSucceeded = false` on failure and uses it
to decide whether to cache the result. Swallowing here would make the service cache
degraded results as if they were real.

**`GeoapifyClient`** — same treatment: catch transient failures, log Warning with the
operation and query, rethrow. Geoapify data is essential, so the use case must still
decide (serve stale, or fail the request).

**`ResilientDistributedCache`** — `Guard`/`GuardAsync` already swallow by design; add a
Warning log naming the failed operation. Both are currently `static`, so they become
instance methods to reach an injected `ILogger<ResilientDistributedCache>`.

### All five Application catch blocks stay

Each encodes genuine use-case policy, not missing error handling. Each keeps its `catch`,
gains a comment noting the failure is already logged in the adapter, and gains no logger:

| Site | Policy |
|---|---|
| `AuthService` (`SendVerificationEmailAsync`) | F4/US1: registration succeeds regardless; resend is a separate step |
| `DestinationService:158` (`GetOrFetchAsync<T>`) | stale-better-than-down: serve the expired cache entry |
| `DestinationService:288` | fall back to the provider's own image; do **not** cache a failed search |
| `DestinationService:361` | serve stale details if present, else return null |
| `TripService:329` | save the destination without a photo |

The net effect is that no Application code is deleted — the value here is entirely that
every one of these failures is now recorded exactly once, at its source.

### Log levels

Warning for every case above — the system degraded but handled it. Not Error: nothing is
broken from the caller's perspective. `ExceptionHandlingMiddleware`'s existing
`LogError` for unhandled exceptions is unchanged.

### Explicitly out of scope

Logging **business events** (user registered, trip created). That would require
`Microsoft.Extensions.Logging.Abstractions` in Application and a logger on each service —
a separate decision, deliberately not bundled here.

---

## 2. Read JWT config through the options pattern

### Problem

`Program.cs:35-60` reads `Jwt:Key` eagerly off `builder.Configuration` in a top-level
statement. That runs **before** any `WebApplicationFactory` hook can layer in test
configuration, which is why `CustomWebApplicationFactory` has to set `Jwt__Key` as a real
**process environment variable** in its constructor.

### Change

Bind and validate through options, with `ValidateOnStart()` preserving the current
fail-fast-at-startup behaviour and both existing error messages verbatim:

```csharp
builder.Services.AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection(JwtSettings.SectionName))
    .Validate(s => !string.IsNullOrWhiteSpace(s.Key),
        "Jwt__Key is not configured. Copy backend/src/TripPlanner.WebApi/.env.example to .env "
        + "and set Jwt__Key to a random secret of at least 32 characters.")
    .Validate(s => Encoding.UTF8.GetByteCount(s.Key) >= JwtSettings.MinKeyBytes,
        $"Jwt__Key is too short. HMAC-SHA256 signing requires at least {JwtSettings.MinKeyBytes} bytes "
        + "— set Jwt__Key in backend/src/TripPlanner.WebApi/.env to a longer random secret.")
    .ValidateOnStart();
```

`AddJwtBearer` then gets its signing key from the bound settings at DI-resolution time,
rather than from a variable captured during startup — no new class required:

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((bearer, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.Zero,
        };
    });
```

This removes **three** eager reads, not one: `jwtSection["Key"]`, `jwtSection["Issuer"]`,
and `jwtSection["Audience"]`. The `?? JwtSettings.DefaultIssuer` / `?? DefaultAudience`
fallbacks disappear too — `JwtSettings` already defaults both properties, so the bound
object carries them and the null-coalescing was duplicating that default in a second place.

Afterwards `Program.cs` holds no `jwtSection` variable, no `jwtKey`/`jwtKeyBytes`, and no
`signingKey` — roughly 25 lines of startup code become one options registration.

Note the length message loses its actual-byte-count interpolation, since the value is no
longer in scope where the message is declared. Acceptable; the setting name and the
required minimum are what a reader needs.

### Then simplify the test factory

With no eager read, `CustomWebApplicationFactory` supplies `Jwt:Key` through
`ConfigureAppConfiguration` (or `UseSetting`) instead of `Environment.SetEnvironmentVariable`.
Deleting the constructor removes the mechanism that most of the CLAUDE.md testing note
exists to explain.

**Also verify:** the constructor sets `ConnectionStrings__Postgres` too, but nothing reads
it eagerly — `AddPersistence` passes it to `UseNpgsql` lazily, and the factory removes
that registration outright. Delete the line and run the tests: if green, it was never
needed. If red, keep it and record why in a comment.

**Then trim CLAUDE.md**, whose testing section documents workarounds this removes. Keep
only what remains true — the `_databaseName`-must-be-a-field warning and the
"remove both DbContext registrations" warning are unaffected and stay.

---

## 3. Declare `BaseEntity.Id` as `ValueGeneratedNever()`

### Problem

`BaseEntity` self-assigns `Id = Guid.NewGuid()`, but EF is configured (by convention) to
treat a `Guid` key as generated on add. So when EF discovers a new child with a
non-default key, it classifies the row as **existing** and issues an UPDATE instead of an
INSERT. This is what forced `ITripRepository.AddDay`/`AddItem` to exist — proven by 21
test failures during the previous refactor.

### Change

State the truth in the model: the application supplies the key.

Add to each of the five entity configurations (`UserConfiguration`, `TripConfiguration`,
`ItineraryDayConfiguration`, `ItineraryItemConfiguration`, `DestinationConfiguration`):

```csharp
        // BaseEntity assigns its own Guid, so EF must not treat the key as
        // store-generated — otherwise a new child with a set key is mistaken
        // for an existing row and saved as an UPDATE.
        builder.Property(x => x.Id).ValueGeneratedNever();
```

Five explicit lines are preferred over one reflection loop over `modelBuilder.Model`,
per the project's "explicit code over clever abstractions" rule.

### Then delete the workaround

Remove `AddDay` and `AddItem` from `ITripRepository` and `TripRepository`, and their two
call sites in `TripService` (`RegenerateDays`, `AddDestinationAsync`), leaving only the
`trip.Days.Add(...)` / `trip.Items.Add(...)` collection mutations.

### Verification and risk

This is the gate: the same four tests that caught it last time must pass —
`TripServiceTests`' date-change and add-destination cases,
`TripsEndpointsTests.UpdateTrip_RenameAndSetDates_RegeneratesItineraryDays`, and
`AddDestination_ThenRemove_RoundTrips`. If they fail, `ValueGeneratedNever` was not
sufficient: restore `AddDay`/`AddItem` and stop.

**Migration:** run `dotnet ef migrations add SetIdValueGeneratedNever`. For a Guid primary
key on Postgres the column DDL does not change, so expect an **empty `Up`/`Down`**. Keep
the empty migration anyway, with a comment stating it is a metadata-only model change —
that keeps `ApplicationDbContextModelSnapshot` in sync, which is what later migrations
diff against.

---

## 4. Split `UpdateItineraryItemAsync`

### Problem

56 lines against the project's 30–40 guideline, doing four things: validate, reject
duplicates on the target day, move the item and resequence *two* buckets, then save.

### Change

Extract the move into a private method, leaving the public method as a readable sequence
of guards followed by one call:

```csharp
    /// <summary>
    /// Inserts the item at the requested position in the target bucket and renumbers
    /// both affected buckets 0..n so SortOrder stays dense. Position is clamped, so
    /// "99" means last.
    /// </summary>
    private static void MoveItem(Trip trip, ItineraryItem item, Guid? targetDayId, int sortOrder)
```

`MoveItem` absorbs the source/target bucket handling and both `Resequence` calls.
`Resequence` stays as it is.

No behaviour change, so the existing tests are the whole verification:
`UpdateItineraryItemAsync_*` in `TripServiceTests` plus
`TripsEndpointsTests.UpdateItineraryItem_ChangeSortOrder_ReordersSavedPlaces`.

---

## Also: a leftover from the previous refactor

`TripPlanner.Application.csproj`'s header comment still says the layer defines
`IRepository<T>` and `IUnitOfWork`, both deleted in `64de71b`. Fix it to name the three
aggregate repositories. One line, folded into whichever section lands first.

---

## Verification for every section

- `dotnet build` clean: 0 warnings, 0 errors.
- `dotnet test` green: **103 tests** (82 Application + 21 WebApi). No section adds or
  removes a test except where noted — §1 removes no tests, and §3 removes none.
- `TripPlanner.Application` must still contain no `Microsoft.EntityFrameworkCore`
  reference, and after §1 must still contain no logging package reference.
