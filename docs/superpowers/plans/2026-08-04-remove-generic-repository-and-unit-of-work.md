# Remove Generic Repository and Unit of Work — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete `IRepository<T>`/`Repository<T>` and `IUnitOfWork`/`UnitOfWork`, replacing them with three standalone aggregate-scoped repositories whose write methods persist their own changes.

**Architecture:** Each repository owns one aggregate root (`User`, `Trip`, `Destination`) and inherits nothing. Write methods (`AddAsync`, `UpdateAsync`, `RemoveItemAsync`) call `SaveChanges` internally, so the Application layer never learns that saving exists. Multi-entity atomicity is preserved by scoping each save to a tracked aggregate — `Trip.Days` and `Trip.Items` are written in one `SaveChanges` with the `Trip`. The unique-violation → `ConcurrencyException` translation moves into the `ApplicationDbContext.SaveChangesAsync` override that already exists for audit stamping.

**Tech Stack:** .NET 10, EF Core 10 (Npgsql), xUnit, Moq, FluentValidation.

**Spec:** `docs/superpowers/specs/2026-08-04-remove-generic-repository-and-unit-of-work-design.md`

## Global Constraints

- The Application layer must never reference EF Core. No `using Microsoft.EntityFrameworkCore` in `TripPlanner.Application`.
- Repository interfaces live in `Application/Common/Interfaces/`; implementations in `Infrastructure/Persistence/Repositories/`.
- No interface inherits from another. No generic base classes.
- `CancellationToken cancellationToken = default` is the last parameter on every async repository method.
- Every task ends with `dotnet build` clean (0 warnings, 0 errors) and `dotnet test` fully green (**106 tests** at start: 85 Application + 21 WebApi).
- Run all commands from `backend/`.
- Commit messages: conventional prefix, short subject, **no body**.
- Do not commit unless the task's commit step says to.

## This Is A Refactor: Tests Are The Specification

There is no new behaviour here, so the usual red-green cycle does not apply to most
tasks. The existing 106 tests are the characterization suite. The cycle per task is:

1. Confirm green **before** touching anything (so a later failure is attributable).
2. Make the change.
3. Confirm green **after**.

Where a task adds a genuinely new test, the step says so explicitly and states whether
it should pass immediately (characterization) or fail first (new behaviour).

## File Structure

**Created:**
- `tests/TripPlanner.Application.Tests/Persistence/ApplicationDbContextTests.cs` — protects the `SaveChangesAsync` override (audit stamping); currently untested

**Modified:**
- `src/TripPlanner.Infrastructure/Persistence/ApplicationDbContext.cs` — gains the concurrency translation
- `src/TripPlanner.Application/Common/Interfaces/IUserRepository.cs` — standalone
- `src/TripPlanner.Application/Common/Interfaces/IDestinationRepository.cs` — standalone
- `src/TripPlanner.Application/Common/Interfaces/ITripRepository.cs` — standalone
- `src/TripPlanner.Infrastructure/Persistence/Repositories/UserRepository.cs` — no base class
- `src/TripPlanner.Infrastructure/Persistence/Repositories/DestinationRepository.cs` — no base class
- `src/TripPlanner.Infrastructure/Persistence/Repositories/TripRepository.cs` — no base class
- `src/TripPlanner.Application/Features/Auth/AuthService.cs` — drops `IUnitOfWork`
- `src/TripPlanner.Application/Features/Trips/TripService.cs` — drops 3 dependencies
- `src/TripPlanner.Application/Common/Exceptions/ConcurrencyException.cs` — doc comment
- `src/TripPlanner.Infrastructure/DependencyInjection.cs` — DI cleanup
- `tests/TripPlanner.Application.Tests/Auth/AuthServiceTests.cs` — construction
- `tests/TripPlanner.Application.Tests/Trips/TripServiceTests.cs` — construction
- `CLAUDE.md` — documents the removed abstraction as current

**Deleted:**
- `src/TripPlanner.Application/Common/Interfaces/IRepository.cs`
- `src/TripPlanner.Application/Common/Interfaces/IUnitOfWork.cs`
- `src/TripPlanner.Infrastructure/Persistence/Repositories/Repository.cs`
- `src/TripPlanner.Infrastructure/Persistence/UnitOfWork.cs`
- `tests/TripPlanner.Application.Tests/Repositories/RepositoryTests.cs`

**Not modified:** `DestinationService.cs` only reads (`GetByProviderIdReadOnlyAsync`), so it needs no change.

---

### Task 1: Move the concurrency translation into ApplicationDbContext

**Files:**
- Create: `tests/TripPlanner.Application.Tests/Persistence/ApplicationDbContextTests.cs`
- Modify: `src/TripPlanner.Infrastructure/Persistence/ApplicationDbContext.cs:35-46`
- Modify: `src/TripPlanner.Infrastructure/Persistence/UnitOfWork.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `ApplicationDbContext.SaveChangesAsync` now throws `ConcurrencyException` on a unique-index violation. Every later task relies on this — repositories call `_context.SaveChangesAsync(ct)` and get the translation for free.

- [ ] **Step 1: Confirm the suite is green before starting**

Run: `dotnet test`
Expected: `Passed! - Failed: 0, Passed: 85` and `Passed! - Failed: 0, Passed: 21`

- [ ] **Step 2: Add a characterization test for audit stamping**

`UpdatedAt` stamping has **no test today**, and this task edits the method that does it.
This test should **PASS immediately** — it is not a red-green step. Its job is to fail if
Step 4 breaks stamping.

Create `tests/TripPlanner.Application.Tests/Persistence/ApplicationDbContextTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TripPlanner.Domain.Entities;
using TripPlanner.Infrastructure.Persistence;
using Xunit;

namespace TripPlanner.Application.Tests.Persistence;

/// <summary>
/// Covers the SaveChangesAsync override: audit stamping, and the fact that an
/// ordinary save is left alone. The unique-violation translation it also performs
/// is not reachable here — the InMemory provider does not enforce unique indexes.
/// </summary>
public class ApplicationDbContextTests
{
    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task SaveChangesAsync_OnInsert_LeavesUpdatedAtNull()
    {
        using var db = CreateDb();

        db.Destinations.Add(new Destination { ProviderId = "geo-1", Name = "Golden Bridge" });
        await db.SaveChangesAsync();

        var saved = await db.Destinations.SingleAsync();
        Assert.Null(saved.UpdatedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_OnModify_StampsUpdatedAt()
    {
        using var db = CreateDb();
        var destination = new Destination { ProviderId = "geo-1", Name = "Golden Bridge" };
        db.Destinations.Add(destination);
        await db.SaveChangesAsync();

        destination.Name = "Golden Bridge (renamed)";
        await db.SaveChangesAsync();

        Assert.NotNull(destination.UpdatedAt);
    }
}
```

- [ ] **Step 3: Run the new tests — they must already pass**

Run: `dotnet test --filter "FullyQualifiedName~ApplicationDbContextTests"`
Expected: `Passed! - Failed: 0, Passed: 2`

If `SaveChangesAsync_OnModify_StampsUpdatedAt` fails here, stop — the override is
already broken and that is a separate bug.

- [ ] **Step 4: Move the translation into the override**

Replace the whole `SaveChangesAsync` override in `ApplicationDbContext.cs` (it becomes
`async`), and add the private helper below it:

```csharp
    /// <summary>
    /// Stamps audit timestamps, and translates a unique-index violation into
    /// <see cref="ConcurrencyException"/> so the Application layer never sees
    /// EF Core's DbUpdateException.
    ///
    /// Only unique violations are translated. Callers turn a ConcurrencyException
    /// into "this destination is already in that part of the trip", so translating
    /// (say) a length violation too would report the wrong thing to the user.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ConcurrencyException("A concurrent write conflicted with this save.", ex);
        }
    }

    /// <summary>
    /// Postgres-specific by necessity: EF Core offers no provider-agnostic way to
    /// ask "was this a unique violation?", and Postgres is the only supported
    /// database outside tests.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
```

Add these two usings at the top of the file:

```csharp
using Npgsql;
using TripPlanner.Application.Common.Exceptions;
```

- [ ] **Step 5: Reduce UnitOfWork to a pass-through**

The translation now happens one level down, so `UnitOfWork`'s own try/catch is dead.
Replace the entire contents of `UnitOfWork.cs`:

```csharp
using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.Infrastructure.Persistence;

/// <summary>
/// Pass-through to <see cref="ApplicationDbContext.SaveChangesAsync"/>, which does
/// the audit stamping and concurrency translation. Deleted in a later task once no
/// service depends on it.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
```

- [ ] **Step 6: Build and run the full suite**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

Run: `dotnet test`
Expected: `Passed: 87` (Application: 85 + 2 new) and `Passed: 21`

- [ ] **Step 7: Commit**

```bash
git add backend/src/TripPlanner.Infrastructure/Persistence/ApplicationDbContext.cs backend/src/TripPlanner.Infrastructure/Persistence/UnitOfWork.cs backend/tests/TripPlanner.Application.Tests/Persistence/ApplicationDbContextTests.cs
git commit -m "refactor: move concurrency translation into the DbContext override"
```

---

### Task 2: Auth slice — standalone IUserRepository

**Files:**
- Modify: `src/TripPlanner.Application/Common/Interfaces/IUserRepository.cs`
- Modify: `src/TripPlanner.Infrastructure/Persistence/Repositories/UserRepository.cs`
- Modify: `src/TripPlanner.Application/Features/Auth/AuthService.cs`
- Test: `tests/TripPlanner.Application.Tests/Auth/AuthServiceTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext.SaveChangesAsync` (Task 1) for the translation
- Produces: `IUserRepository` with `GetByIdAsync(Guid, CancellationToken)`, `GetByEmailAsync(string, CancellationToken)`, `ExistsByEmailAsync(string, CancellationToken)`, `AddAsync(User, CancellationToken)`, `UpdateAsync(User, CancellationToken)`. All return `Task`/`Task<T>`. `AddAsync` and `UpdateAsync` save.

- [ ] **Step 1: Confirm green**

Run: `dotnet test`
Expected: `Passed: 87` and `Passed: 21`

- [ ] **Step 2: Rewrite IUserRepository as standalone**

`GetByIdAsync` came from `IRepository<User>` and must now be declared explicitly.
Replace the whole file:

```csharp
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Persistence for the User aggregate. Write methods save their own changes —
/// there is no separate unit of work.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Inserts the user and persists immediately.</summary>
    Task AddAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>Persists pending changes to an already-loaded user.</summary>
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Rewrite UserRepository with no base class**

Replace the whole file:

```csharp
using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;

    public UserRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        await _context.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        await _context.Users.AnyAsync(u => u.Email == email, cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default) =>
        await _context.SaveChangesAsync(cancellationToken);
}
```

Note: `GetByIdAsync` uses `FirstOrDefaultAsync` rather than the old `FindAsync`.
`FindAsync` returns an already-tracked instance without querying; `FirstOrDefaultAsync`
always queries. Both are correct for `VerifyEmailAsync`, which loads then mutates.

`UpdateAsync` ignores its `user` parameter — the entity is already tracked. The parameter
stays for a readable call site (`_users.UpdateAsync(user, ct)`) and to keep the interface
honest about what is being saved.

- [ ] **Step 4: Update AuthService**

In `AuthService.cs` make four edits.

(a) Delete the field:
```csharp
    private readonly IUnitOfWork _unitOfWork;
```

(b) Delete the constructor parameter `IUnitOfWork unitOfWork,` and the assignment `_unitOfWork = unitOfWork;`

(c) In `RegisterAsync`, replace:
```csharp
        _users.Add(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
```
with:
```csharp
        await _users.AddAsync(user, cancellationToken);
```

(d) In `VerifyEmailAsync`, replace:
```csharp
        if (!user.IsEmailVerified)
        {
            user.IsEmailVerified = true;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
```
with:
```csharp
        if (!user.IsEmailVerified)
        {
            user.IsEmailVerified = true;
            await _users.UpdateAsync(user, cancellationToken);
        }
```

- [ ] **Step 5: Fix the stale doc banner**

The class banner lists `IUnitOfWork` as an interface to depend on. In the `<summary>`,
replace:
```
///   * depend on INTERFACES from the Application layer (IUserRepository,
///     IUnitOfWork, IPasswordHasher, IJwtTokenGenerator) — never on
///     EF/Infrastructure types;
```
with:
```
///   * depend on INTERFACES from the Application layer (IUserRepository,
///     IPasswordHasher, IJwtTokenGenerator) — never on EF/Infrastructure types;
```

- [ ] **Step 6: Update the test construction**

In `AuthServiceTests.cs`, in `CreateSut`, delete this line:
```csharp
        var unitOfWork = new UnitOfWork(db);
```

and change the constructor call from:
```csharp
        return new AuthService(
            users, unitOfWork, hasher, tokenGenerator.Object, email.Object, appUrls.Object,
```
to:
```csharp
        return new AuthService(
            users, hasher, tokenGenerator.Object, email.Object, appUrls.Object,
```

- [ ] **Step 7: Build and test**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

Run: `dotnet test`
Expected: `Passed: 87` and `Passed: 21`

The Auth WebApi tests (register → verify → login) exercise `AddAsync` and `UpdateAsync`
end to end, so a broken save shows up here.

- [ ] **Step 8: Commit**

```bash
git add backend/src/TripPlanner.Application/Common/Interfaces/IUserRepository.cs backend/src/TripPlanner.Infrastructure/Persistence/Repositories/UserRepository.cs backend/src/TripPlanner.Application/Features/Auth/AuthService.cs backend/tests/TripPlanner.Application.Tests/Auth/AuthServiceTests.cs
git commit -m "refactor: make IUserRepository standalone and self-saving"
```

---

### Task 3: Destinations slice — standalone IDestinationRepository

**Files:**
- Modify: `src/TripPlanner.Application/Common/Interfaces/IDestinationRepository.cs`
- Modify: `src/TripPlanner.Infrastructure/Persistence/Repositories/DestinationRepository.cs`
- Modify: `src/TripPlanner.Application/Features/Trips/TripService.cs` (only `GetOrCreateDestinationAsync`)

**Interfaces:**
- Consumes: `ApplicationDbContext.SaveChangesAsync` (Task 1)
- Produces: `IDestinationRepository` with `GetByProviderIdAsync(string, CancellationToken)`, `GetByProviderIdReadOnlyAsync(string, CancellationToken)`, `AddAsync(Destination, CancellationToken)`. `AddAsync` saves, and on a unique-violation conflict detaches the failed entity before rethrowing `ConcurrencyException`.

**Why TripService changes here:** dropping `IRepository<Destination>` removes the `Add`
and `Remove` methods that `TripService.GetOrCreateDestinationAsync` currently calls, so
that one method must move in the same commit or the build breaks. The rest of
`TripService` is Task 4.

- [ ] **Step 1: Confirm green**

Run: `dotnet test`
Expected: `Passed: 87` and `Passed: 21`

- [ ] **Step 2: Rewrite IDestinationRepository as standalone**

Replace the whole file:

```csharp
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
```

- [ ] **Step 3: Rewrite DestinationRepository with no base class**

Replace the whole file:

```csharp
using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Repositories;

public class DestinationRepository : IDestinationRepository
{
    private readonly ApplicationDbContext _context;

    public DestinationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Destination?> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default) =>
        await _context.Destinations.FirstOrDefaultAsync(d => d.ProviderId == providerId, cancellationToken);

    public async Task<Destination?> GetByProviderIdReadOnlyAsync(string providerId, CancellationToken cancellationToken = default) =>
        await _context.Destinations.AsNoTracking().FirstOrDefaultAsync(d => d.ProviderId == providerId, cancellationToken);

    public async Task AddAsync(Destination destination, CancellationToken cancellationToken = default)
    {
        _context.Destinations.Add(destination);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            // A concurrent request inserted this ProviderId first. Detach our losing
            // copy so the caller can re-fetch the winner on a clean change tracker.
            _context.Entry(destination).State = EntityState.Detached;
            throw;
        }
    }
}
```

- [ ] **Step 4: Update TripService.GetOrCreateDestinationAsync**

Replace the tail of that method — from `_destinations.Add(destination);` to the end of
the catch block — with:

```csharp
        try
        {
            await _destinations.AddAsync(destination, cancellationToken);
            return destination;
        }
        catch (ConcurrencyException)
        {
            // Unique index on ProviderId: a concurrent request inserted the same
            // place first. Use the winner's row instead of ours.
            return await _destinations.GetByProviderIdAsync(providerId, cancellationToken)
                ?? throw new InvalidOperationException($"Destination '{providerId}' vanished after a concurrency conflict.");
        }
```

The old `_destinations.Remove(destination);` line inside the catch is deleted — the
repository detaches its own failed entity now.

- [ ] **Step 5: Build and test**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

Run: `dotnet test`
Expected: `Passed: 87` and `Passed: 21`

`TripServiceTests`' add-destination cases and `DestinationServiceTests`' cache-fallback
cases both cover this path.

- [ ] **Step 6: Commit**

```bash
git add backend/src/TripPlanner.Application/Common/Interfaces/IDestinationRepository.cs backend/src/TripPlanner.Infrastructure/Persistence/Repositories/DestinationRepository.cs backend/src/TripPlanner.Application/Features/Trips/TripService.cs
git commit -m "refactor: make IDestinationRepository standalone and self-saving"
```

---

### Task 4: Trips slice — standalone ITripRepository

The largest task. `TripService` drops three dependencies (12 → 9) and six methods change.

**Files:**
- Modify: `src/TripPlanner.Application/Common/Interfaces/ITripRepository.cs`
- Modify: `src/TripPlanner.Infrastructure/Persistence/Repositories/TripRepository.cs`
- Modify: `src/TripPlanner.Application/Features/Trips/TripService.cs`
- Test: `tests/TripPlanner.Application.Tests/Trips/TripServiceTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext.SaveChangesAsync` (Task 1)
- Produces: `ITripRepository` with `GetSummaryRowsForUserAsync(Guid, CancellationToken)`, `GetDetailsAsync(Guid, Guid, CancellationToken)`, `GetForUpdateAsync(Guid, Guid, CancellationToken)`, `DayBelongsToTripAsync(Guid, Guid, CancellationToken)`, `GetOwnedItemAsync(Guid, Guid, Guid, CancellationToken)`, `AddAsync(Trip, CancellationToken)`, `UpdateAsync(Trip, CancellationToken)`, `RemoveItemAsync(ItineraryItem, CancellationToken)`.

- [ ] **Step 1: Confirm green**

Run: `dotnet test`
Expected: `Passed: 87` and `Passed: 21`

- [ ] **Step 2: Rewrite ITripRepository as standalone**

`GetTrackedWithFullGraphAsync` is renamed `GetForUpdateAsync` — "tracked" is EF
vocabulary and does not belong in an Application interface name. Replace the whole file:

```csharp
using TripPlanner.Application.Features.Trips;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Persistence for the Trip aggregate (Trip + ItineraryDay + ItineraryItem).
/// Days and items have no meaning outside the trip that owns them, so they are
/// reached through the trip rather than through repositories of their own, and a
/// single save covers the whole graph.
/// </summary>
public interface ITripRepository
{
    /// <summary>Projected list rows for GetMyTripsAsync — see TripMappings.ToSummaryRowExpression.</summary>
    Task<List<TripSummaryRow>> GetSummaryRowsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Read-only, full graph (Days.Items.Destination + Items.Destination) — for GetTripAsync.</summary>
    Task<Trip?> GetDetailsAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Full graph loaded for mutation; changes to Days/Items are persisted by UpdateAsync.</summary>
    Task<Trip?> GetForUpdateAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Whether the given day belongs to the given trip (checked before scheduling into it).</summary>
    Task<bool> DayBelongsToTripAsync(Guid itineraryDayId, Guid tripId, CancellationToken cancellationToken = default);

    /// <summary>The item, only if it belongs to a trip owned by userId (NFR 6).</summary>
    Task<ItineraryItem?> GetOwnedItemAsync(Guid tripId, Guid itemId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Inserts the trip and persists immediately.</summary>
    Task AddAsync(Trip trip, CancellationToken cancellationToken = default);

    /// <summary>Persists every pending change to the trip and its days/items in one save.</summary>
    Task UpdateAsync(Trip trip, CancellationToken cancellationToken = default);

    /// <summary>Deletes a single itinerary item and persists immediately.</summary>
    Task RemoveItemAsync(ItineraryItem item, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Rewrite TripRepository with no base class**

Replace the whole file:

```csharp
using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Trips;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Persistence.Repositories;

public class TripRepository : ITripRepository
{
    private readonly ApplicationDbContext _context;

    public TripRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<TripSummaryRow>> GetSummaryRowsForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await _context.Trips
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(TripMappings.ToSummaryRowExpression)
            .ToListAsync(cancellationToken);

    public async Task<Trip?> GetDetailsAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
        await _context.Trips
            .AsNoTracking()
            .Include(t => t.Days).ThenInclude(d => d.Items).ThenInclude(i => i.Destination)
            .Include(t => t.Items).ThenInclude(i => i.Destination)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, cancellationToken);

    public async Task<Trip?> GetForUpdateAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
        await _context.Trips
            .Include(t => t.Days).ThenInclude(d => d.Items)
            .Include(t => t.Items).ThenInclude(i => i.Destination)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, cancellationToken);

    public async Task<bool> DayBelongsToTripAsync(Guid itineraryDayId, Guid tripId, CancellationToken cancellationToken = default) =>
        await _context.ItineraryDays
            .AnyAsync(d => d.Id == itineraryDayId && d.TripId == tripId, cancellationToken);

    public async Task<ItineraryItem?> GetOwnedItemAsync(Guid tripId, Guid itemId, Guid userId, CancellationToken cancellationToken = default) =>
        await _context.ItineraryItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.TripId == tripId && i.Trip!.UserId == userId, cancellationToken);

    public async Task AddAsync(Trip trip, CancellationToken cancellationToken = default)
    {
        _context.Trips.Add(trip);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Trip trip, CancellationToken cancellationToken = default) =>
        await _context.SaveChangesAsync(cancellationToken);

    public async Task RemoveItemAsync(ItineraryItem item, CancellationToken cancellationToken = default)
    {
        _context.ItineraryItems.Remove(item);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Strip the dead dependencies from TripService**

Delete these three fields:
```csharp
    private readonly IRepository<ItineraryDay> _itineraryDays;
    private readonly IRepository<ItineraryItem> _itineraryItems;
    private readonly IUnitOfWork _unitOfWork;
```

Delete these three constructor parameters:
```csharp
        IRepository<ItineraryDay> itineraryDays,
        IRepository<ItineraryItem> itineraryItems,
        IUnitOfWork unitOfWork,
```

Delete these three assignments:
```csharp
        _itineraryDays = itineraryDays;
        _itineraryItems = itineraryItems;
        _unitOfWork = unitOfWork;
```

- [ ] **Step 5: Update the six call sites**

(a) `CreateTripAsync` — replace:
```csharp
        _trips.Add(trip);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
```
with:
```csharp
        await _trips.AddAsync(trip, cancellationToken);
```

(b) `UpdateTripAsync` — replace `_trips.GetTrackedWithFullGraphAsync(` with
`_trips.GetForUpdateAsync(`, and replace:
```csharp
        await _unitOfWork.SaveChangesAsync(cancellationToken);
```
with:
```csharp
        await _trips.UpdateAsync(trip, cancellationToken);
```

(c) `RegenerateDays` — delete the two repository calls. The removal loop becomes:
```csharp
        foreach (var day in trip.Days.Where(d => !targetDates.Contains(d.Date)).ToList())
        {
            // Mirror the DB's SetNull cascade in memory so the DTO we return
            // already shows these items back in Saved Places.
            foreach (var item in day.Items)
            {
                item.ItineraryDayId = null;
            }

            // Trip.Days is configured OnDelete(Cascade), so removing the day from
            // the tracked collection marks the row deleted.
            trip.Days.Remove(day);
        }
```
and the creation loop becomes:
```csharp
        var existingDates = trip.Days.Select(d => d.Date).ToHashSet();
        foreach (var date in targetDates.Where(d => !existingDates.Contains(d)))
        {
            trip.Days.Add(new ItineraryDay { TripId = trip.Id, Date = date });
        }
```

(d) `AddDestinationAsync` — replace `_trips.GetTrackedWithFullGraphAsync(` with
`_trips.GetForUpdateAsync(`, delete the line `_itineraryItems.Add(item);`, and replace:
```csharp
            await _unitOfWork.SaveChangesAsync(cancellationToken);
```
with:
```csharp
            await _trips.UpdateAsync(trip, cancellationToken);
```

(e) `UpdateItineraryItemAsync` — replace `_trips.GetTrackedWithFullGraphAsync(` with
`_trips.GetForUpdateAsync(`, and replace:
```csharp
            await _unitOfWork.SaveChangesAsync(cancellationToken); // single save: both buckets move atomically
```
with:
```csharp
            await _trips.UpdateAsync(trip, cancellationToken); // single save: both buckets move atomically
```

(f) `RemoveDestinationAsync` — replace:
```csharp
        _itineraryItems.Remove(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
```
with:
```csharp
        await _trips.RemoveItemAsync(item, cancellationToken);
```

- [ ] **Step 6: Update the test construction**

In `TripServiceTests.cs`, in `CreateSut`, change:
```csharp
        return new TripService(
            new TripRepository(db),
            new Repository<Domain.Entities.ItineraryDay>(db),
            new Repository<Domain.Entities.ItineraryItem>(db),
            new DestinationRepository(db),
            new UnitOfWork(db),
            currentUser.Object,
```
to:
```csharp
        return new TripService(
            new TripRepository(db),
            new DestinationRepository(db),
            currentUser.Object,
```

- [ ] **Step 7: Build and test — this is the risk gate**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

Run: `dotnet test`
Expected: `Passed: 87` and `Passed: 21`

**If day or item inserts fail here**, the spec's stated risk has materialised: EF is not
picking up entities added to `trip.Days` / `trip.Items` as `Added`. The watch-list is
`TripServiceTests`' date-change and add-destination cases, plus
`TripsEndpointsTests.UpdateTrip_RenameAndSetDates_RegeneratesItineraryDays` and
`AddDestination_ThenRemove_RoundTrips`.

Fix, if needed: add `Task AddDayAsync(ItineraryDay day, CancellationToken)` and
`Task AddItemAsync(ItineraryItem item, CancellationToken)` to `ITripRepository` that call
`_context.ItineraryDays.Add(day)` / `_context.ItineraryItems.Add(item)` **without**
saving, and call them alongside the collection mutation. Do not build these unless a test
actually fails.

- [ ] **Step 8: Commit**

```bash
git add backend/src/TripPlanner.Application/Common/Interfaces/ITripRepository.cs backend/src/TripPlanner.Infrastructure/Persistence/Repositories/TripRepository.cs backend/src/TripPlanner.Application/Features/Trips/TripService.cs backend/tests/TripPlanner.Application.Tests/Trips/TripServiceTests.cs
git commit -m "refactor: make ITripRepository standalone and self-saving"
```

---

### Task 5: Delete the dead abstraction and update docs

**Files:**
- Delete: `src/TripPlanner.Application/Common/Interfaces/IRepository.cs`
- Delete: `src/TripPlanner.Application/Common/Interfaces/IUnitOfWork.cs`
- Delete: `src/TripPlanner.Infrastructure/Persistence/Repositories/Repository.cs`
- Delete: `src/TripPlanner.Infrastructure/Persistence/UnitOfWork.cs`
- Delete: `tests/TripPlanner.Application.Tests/Repositories/RepositoryTests.cs`
- Modify: `src/TripPlanner.Infrastructure/DependencyInjection.cs:82-86`
- Modify: `src/TripPlanner.Application/Common/Exceptions/ConcurrencyException.cs`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: Tasks 2-4 must all be complete — nothing may reference the deleted types.
- Produces: nothing.

- [ ] **Step 1: Confirm nothing still references the four types**

Run: `grep -rn "IRepository<\|IUnitOfWork\|UnitOfWork\|Repository<" --include=*.cs src tests | grep -v "/obj/"`
Expected: only the five files being deleted, plus `RepositoryTests.cs`. If any other
file appears, stop — an earlier task is incomplete.

- [ ] **Step 2: Delete the five files**

```bash
git rm backend/src/TripPlanner.Application/Common/Interfaces/IRepository.cs backend/src/TripPlanner.Application/Common/Interfaces/IUnitOfWork.cs backend/src/TripPlanner.Infrastructure/Persistence/Repositories/Repository.cs backend/src/TripPlanner.Infrastructure/Persistence/UnitOfWork.cs backend/tests/TripPlanner.Application.Tests/Repositories/RepositoryTests.cs
```

`RepositoryTests.cs` tested only the generic abstraction's mechanics (Add / GetById /
GetAll / Remove). It carried no business behaviour and no audit-timestamp coverage — the
latter is now in `ApplicationDbContextTests` from Task 1. `GetAllAsync` had no production
caller at all.

- [ ] **Step 3: Clean up DI registration**

In `DependencyInjection.cs`, delete the comment and the two registrations:
```csharp
        // Generic repository + unit of work (open generic covers IRepository<ItineraryDay>,
        // IRepository<ItineraryItem> directly; entity-specific repositories are
        // registered individually as they're introduced in later tasks).
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
```

The three concrete repository registrations immediately below stay exactly as they are.

- [ ] **Step 4: Repoint the ConcurrencyException doc comment**

Replace:
```csharp
/// Thrown by <see cref="Interfaces.IUnitOfWork.SaveChangesAsync"/> when a save
```
with:
```csharp
/// Thrown when a save
```

- [ ] **Step 5: Update CLAUDE.md**

Two places describe the removed abstraction as current.

In the **Application** bullet under "Clean Architecture layers", replace:
```
Defines `IRepository<T>`/`IUnitOfWork` (generic persistence abstraction — there is no `IApplicationDbContext`)
```
with:
```
Defines one repository interface per aggregate (`IUserRepository`, `ITripRepository`, `IDestinationRepository`) whose write methods save their own changes — there is no generic `IRepository<T>`, no unit of work, and no `IApplicationDbContext`
```

In the **"Concurrency via unique index + catch/retry"** bullet, replace:
```
Read-then-write code against one of these (e.g. upserting a `Destination`) must catch `DbUpdateException` and retry/re-fetch — see `TripService.AddDestinationAsync` for the pattern.
```
with:
```
`ApplicationDbContext.SaveChangesAsync` translates a Postgres unique violation into `ConcurrencyException`, so read-then-write code catches that (never EF's `DbUpdateException`) and re-fetches — see `TripService.GetOrCreateDestinationAsync` and `DestinationRepository.AddAsync`.
```

- [ ] **Step 6: Build and test**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

Run: `dotnet test`
Expected: `Passed: 82` (87 minus the 5 deleted `RepositoryTests`) and `Passed: 21`

- [ ] **Step 7: Verify the Application layer is EF-free**

Run: `grep -rn "Microsoft.EntityFrameworkCore" --include=*.cs src/TripPlanner.Application | grep -v "/obj/"`
Expected: no output.

- [ ] **Step 8: Commit**

```bash
git add -A backend/src backend/tests CLAUDE.md
git commit -m "refactor: delete generic repository and unit of work"
```

---

## Done When

- `IRepository<T>`, `Repository<T>`, `IUnitOfWork`, `UnitOfWork` no longer exist.
- `dotnet build` is clean; `dotnet test` reports **82 + 21 = 103** passing.
- `TripService` has 9 constructor dependencies, `AuthService` has 8.
- No `Microsoft.EntityFrameworkCore` reference anywhere in `TripPlanner.Application`.
- `CLAUDE.md` describes the persistence layer that actually exists.
