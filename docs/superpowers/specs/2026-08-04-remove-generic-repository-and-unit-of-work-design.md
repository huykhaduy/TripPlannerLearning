# Remove the generic repository and unit of work

**Date:** 2026-08-04
**Status:** approved (design), not yet implemented

## Goal

Delete the generic repository (`IRepository<T>` / `Repository<T>`) and the unit of work
(`IUnitOfWork` / `UnitOfWork`), replacing them with three focused, intention-revealing
repository interfaces whose write methods persist their own changes.

The Application layer must still never reference EF Core.

## Why

Both patterns are on the project's explicit "avoid" list. Concretely, they cost us:

- `TripService` injects `IRepository<ItineraryDay>` and `IRepository<ItineraryItem>` —
  raw table access to entities that are not aggregate roots. `ItineraryDay` has no
  meaning outside the `Trip` that owns it.
- `IUnitOfWork.SaveChangesAsync` is called from **8 sites** (AuthService ×2,
  TripService ×6). It exists only to wrap one `DbContext` method.
- `TripService` carries 12 constructor dependencies, `AuthService` 9.
- `RepositoryTests.cs` (87 lines) tests the generic abstraction rather than any
  behaviour the application actually has.

## Approach: aggregate-scoped repositories that save

Repository write methods perform the save. `SaveChanges` disappears from the
Application layer entirely, and each save is scoped to one aggregate.

```csharp
var trip = await _trips.GetForUpdateAsync(tripId, userId, ct);
trip.Name = request.Name.Trim();
trip.SetDates(request.StartDate, request.EndDate);
RegenerateDays(trip);
await _trips.UpdateAsync(trip, ct);   // one save, whole aggregate, one transaction
```

Atomicity for multi-entity operations (day regeneration deletes and inserts
`ItineraryDay` rows together) is preserved because `Days` and `Items` hang off a
tracked `Trip`: EF writes the whole graph in a single `SaveChanges`.

### Rejected alternatives

- **Repositories expose `SaveChangesAsync()`** — mechanically smaller, but it just
  renames `IUnitOfWork` into three interfaces. The service still coordinates saves.
- **Expose `DbContext` / `IApplicationDbContext` to Application** — forces Application
  to reference EF Core, inverting the dependency rule the architecture currently holds.

## Target interfaces

None inherit from anything. `Add`/`Remove` become `Async` because they now save.

```csharp
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
}

public interface IDestinationRepository
{
    Task<Destination?> GetByProviderIdAsync(string providerId, CancellationToken cancellationToken = default);
    Task<Destination?> GetByProviderIdReadOnlyAsync(string providerId, CancellationToken cancellationToken = default);
    Task AddAsync(Destination destination, CancellationToken cancellationToken = default);
}

public interface ITripRepository
{
    Task<List<TripSummaryRow>> GetSummaryRowsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Trip?> GetDetailsAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default);
    Task<Trip?> GetForUpdateAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default);
    Task<bool> DayBelongsToTripAsync(Guid itineraryDayId, Guid tripId, CancellationToken cancellationToken = default);
    Task<ItineraryItem?> GetOwnedItemAsync(Guid tripId, Guid itemId, Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(Trip trip, CancellationToken cancellationToken = default);
    Task UpdateAsync(Trip trip, CancellationToken cancellationToken = default);
    Task RemoveItemAsync(ItineraryItem item, CancellationToken cancellationToken = default);
}
```

`GetTrackedWithFullGraphAsync` is renamed `GetForUpdateAsync`: "tracked" is EF
vocabulary and does not belong in an Application-layer interface name.

Implementations take `ApplicationDbContext` directly and inherit nothing.

## Where the concurrency translation goes

`ApplicationDbContext` **already overrides** `SaveChangesAsync` to stamp audit
timestamps. The unique-violation → `ConcurrencyException` translation moves into that
existing override, along with the `IsUniqueViolation` helper.

No new abstraction, no base class, no extension method. `ConcurrencyException` keeps
its current meaning, so `TripService`'s three catch blocks and the destination
upsert-retry survive unchanged.

Two doc comments cite the removed types and must be repointed:
`ConcurrencyException` names `IUnitOfWork.SaveChangesAsync`, and `AuthService`'s
reference-implementation banner lists `IUnitOfWork` among the interfaces to depend on.

## Per-service changes

**AuthService** (9 → 8 dependencies): drop `IUnitOfWork`.
`RegisterAsync` calls `_users.AddAsync(user, ct)`; `VerifyEmailAsync` calls
`_users.UpdateAsync(user, ct)`.

**DestinationService**: only reads (`GetByProviderIdReadOnlyAsync`). No change beyond
the interface no longer inheriting `IRepository<Destination>`.

**TripService** (12 → 9 dependencies): drop `IRepository<ItineraryDay>`,
`IRepository<ItineraryItem>`, `IUnitOfWork`.

| Method | Change |
|---|---|
| `CreateTripAsync` | `await _trips.AddAsync(trip, ct)` |
| `UpdateTripAsync` | `await _trips.UpdateAsync(trip, ct)` |
| `RegenerateDays` | drop `_itineraryDays.Add/Remove`; mutate `trip.Days` only |
| `AddDestinationAsync` | `trip.Items.Add(item)`, then `UpdateAsync` inside the existing `ConcurrencyException` catch |
| `UpdateItineraryItemAsync` | `await _trips.UpdateAsync(trip, ct)` inside the existing catch |
| `RemoveDestinationAsync` | `await _trips.RemoveItemAsync(item, ct)` |
| `GetOrCreateDestinationAsync` | `await _destinations.AddAsync(destination, ct)`; on conflict, re-fetch as today |

`RemoveDestinationAsync` keeps its standalone single-row fetch (`GetOwnedItemAsync`)
rather than loading the whole trip graph, so `RemoveItemAsync` stays on the interface.

On a unique-violation conflict, `DestinationRepository.AddAsync` detaches the failed
entity before rethrowing — EF cleanup is an Infrastructure concern, so the service no
longer needs a `Remove` call to undo its own add.

## DI changes

In `AddPersistence`, delete:

```csharp
services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
services.AddScoped<IUnitOfWork, UnitOfWork>();
```

The three concrete repository registrations stay as they are.

## Files deleted

- `Application/Common/Interfaces/IRepository.cs`
- `Application/Common/Interfaces/IUnitOfWork.cs`
- `Infrastructure/Persistence/Repositories/Repository.cs`
- `Infrastructure/Persistence/UnitOfWork.cs`
- `tests/TripPlanner.Application.Tests/Repositories/RepositoryTests.cs`

## Implementation order

Each slice ends with a green build and green tests.

0. **Move the translation** into `ApplicationDbContext.SaveChangesAsync`; reduce
   `UnitOfWork` to a pass-through. Nothing else changes yet.
1. **Auth** — standalone `IUserRepository`, `AuthService`, `AuthServiceTests`.
2. **Destinations** — standalone `IDestinationRepository`, detach-on-conflict in
   `AddAsync`.
3. **Trips** — standalone `ITripRepository`, `TripService`, `TripServiceTests`. Largest
   slice.
4. **Delete** the four dead files plus `RepositoryTests.cs`; clean up DI.

## Risks

**New `ItineraryDay` / `ItineraryItem` rows might not be inserted.** `RegenerateDays`
calls `_itineraryDays.Add(day)` explicitly today, and `AddDestinationAsync` calls
`_itineraryItems.Add(item)`, with a comment claiming EF would otherwise treat the row as
existing (UPDATE, not INSERT) because `BaseEntity` self-assigns its `Guid` key. That key
heuristic applies to `Attach`/`Update` on a detached root, not to change detection on an
already-tracked graph — and `GetForUpdateAsync` returns a tracked trip, so entities added
to `trip.Days` / `trip.Items` should be discovered as `Added`.

This is the one assumption in the design that could be wrong, and it affects both
collections. It is covered by existing tests: `TripServiceTests`' date-change and
add-destination cases, plus `TripsEndpointsTests.UpdateTrip_RenameAndSetDates_RegeneratesItineraryDays`
and `AddDestination_ThenRemove_RoundTrips`. If they fail, the fallback is an explicit
`AddDay` / `AddItem` on `ITripRepository` — decide then, do not pre-build it.

**Day deletion relies on cascade.** `Trip.Days` is configured
`OnDelete(DeleteBehavior.Cascade)`, so removing a day from `trip.Days` on a tracked trip
marks it deleted. Verified in `TripConfiguration.cs`; the in-memory
`item.ItineraryDayId = null` loop in `RegenerateDays` stays, since it mirrors the
`SetNull` cascade for the DTO returned in the same request.

**The translation stays untested.** The EF Core InMemory provider does not enforce
unique indexes and does not throw `DbUpdateException`, so the unique-violation path is
unverifiable by `dotnet test` — before this change and after it. Unchanged risk, not a
new one.

## Out of scope

Deliberately excluded so this stays one reviewable change:

- Constructor bloat from `IValidator<T>` dependencies (4 in `TripService`, 3 in `AuthService`)
- Absent `ILogger` usage and the two silently swallowed exceptions
- Moving `Program.cs`'s direct configuration reads to the options pattern, and the
  `CustomWebApplicationFactory` simplification that unlocks
- Whether `IAuthService` / `ITripService` / `IDestinationService` are worth keeping
