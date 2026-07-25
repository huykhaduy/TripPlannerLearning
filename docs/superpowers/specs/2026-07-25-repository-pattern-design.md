# Repository Pattern — Design

**Date:** 2026-07-25
**Scope:** Introduce a Repository + Unit of Work abstraction between the
Application-layer feature services (`AuthService`, `TripService`,
`DestinationService`) and EF Core, replacing today's direct injection of
`IApplicationDbContext` (which exposes raw `DbSet<T>` properties). Backend
only — no frontend changes. Purpose is educational: this is a training
capstone (`CLAUDE.md`), and the student wants to see/practice the Repository
pattern applied correctly inside this project's existing Clean Architecture
layering.

## Current state (context)

`IApplicationDbContext` (`backend/src/TripPlanner.Application/Common/Interfaces/IApplicationDbContext.cs`)
exposes `DbSet<User|Trip|ItineraryDay|Destination|ItineraryItem>` plus
`SaveChangesAsync`. All three feature services inject it directly and run
LINQ/EF calls against the DbSets (`.Include().ThenInclude()` chains, `.Select()`
projections, `.AsNoTracking()`, catching `DbUpdateException` around
`SaveChangesAsync` to translate unique-index violations into
`ConflictException` — see `TripService.cs:83-89`, `218-227`, `362-373`).

[TECHNICAL_SPEC.md:137-141](../../../TECHNICAL_SPEC.md) explicitly documents
this as a deliberate deviation from strict Clean Architecture: "there is no
repository pattern — services query DbSets directly," and notes the
Application layer takes an EF Core package dependency solely to expose
`DbSet<T>` through the interface. This change resolves that deviation.

## Target architecture

```
Application/Common/Interfaces/
  IRepository<T>.cs         generic CRUD, no EF Core dependency
  IUnitOfWork.cs             SaveChangesAsync only
  IUserRepository.cs         IRepository<User> + GetByEmailAsync, ExistsByEmailAsync
  ITripRepository.cs          IRepository<Trip> + GetTripWithDetailsAsync
  IDestinationRepository.cs  IRepository<Destination> + GetByProviderIdAsync

Infrastructure/Persistence/Repositories/
  Repository<T>.cs           generic impl over ApplicationDbContext, no SaveChanges
  UserRepository.cs
  TripRepository.cs
  DestinationRepository.cs
  UnitOfWork.cs               wraps ApplicationDbContext.SaveChangesAsync
```

`ItineraryDay` and `ItineraryItem` get no custom repository interface —
generic `IRepository<ItineraryDay>` / `IRepository<ItineraryItem>` covers
everything `TripService` does with them today (including the explicit
`Add`/`Remove` pairing in `RegenerateDays`, `TripService.cs:162-175`, needed
because `BaseEntity` self-assigns its Guid key).

`IApplicationDbContext` is deleted. Once repositories own all DbSet access,
the raw-DbSet interface is redundant. The concrete `ApplicationDbContext`
(and its `SaveChangesAsync` override that stamps `UpdatedAt` on modified
`BaseEntity` rows) stays in Infrastructure, used only by the repository
implementations and `UnitOfWork` — never referenced outside Infrastructure.
Application's `.csproj` EF Core package reference (justified today by a
comment: "EF Core abstractions only... lets us expose DbSet<T>") is removed,
since no Application-layer interface will reference EF Core types anymore.

## Interfaces

```csharp
// Application/Common/Interfaces/IRepository.cs
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<T>> GetAllAsync(CancellationToken ct = default);
    void Add(T entity);
    void Remove(T entity);
}

// Application/Common/Interfaces/IUnitOfWork.cs
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

`IUserRepository`, `ITripRepository`, `IDestinationRepository` extend
`IRepository<T>` and add exactly the named query methods each service needs
today, replacing inline LINQ:

- `IUserRepository.GetByEmailAsync(string email, ct)` — replaces
  `AuthService.cs:98,112,154` (`_db.Users.FirstOrDefaultAsync(u => u.Email == ...)`).
- `IUserRepository.ExistsByEmailAsync(string email, ct)` — replaces
  `AuthService.cs:66` (`_db.Users.AnyAsync(...)`).
- `ITripRepository.GetTripWithDetailsAsync(Guid id, ct)` — replaces the
  `.Include(t => t.Days).ThenInclude(...)` chain at `TripService.cs:83-89`.
- Other `TripService` read paths that only need scalar/simple projections
  (e.g. the `.Select(...)` at `TripService.cs:64-68`) get their own named
  repository method rather than a generic `IQueryable` escape hatch, to keep
  LINQ composition out of the service layer entirely.
- `IDestinationRepository.GetByProviderIdAsync(string providerId, ct)` —
  replaces `DestinationService.cs:341-343`.

## Unit of Work semantics

Repository methods only stage changes (`Add`/`Remove`) or read; nothing but
`IUnitOfWork.SaveChangesAsync` persists. This preserves today's transactional
shape: `TripService.RegenerateDays` calls several repository `Add`/`Remove`
methods, then one `_unitOfWork.SaveChangesAsync()`, same as it currently
calls several `_db.ItineraryDays.Add/Remove` then one
`_db.SaveChangesAsync()`. The existing `catch (DbUpdateException)`
retry/conflict-translation logic around `SaveChangesAsync` calls
(`TripService.cs:218-227,362-373`) moves to wrap the `IUnitOfWork.SaveChangesAsync`
call instead — same logic, new call site.

## Service refactor

`AuthService`, `TripService`, `DestinationService` drop their
`IApplicationDbContext _db` constructor parameter in favor of the specific
repositories (and `IUnitOfWork`) they need:

- `AuthService`: `IUserRepository`, `IUnitOfWork`.
- `TripService`: `ITripRepository`, `IRepository<ItineraryDay>`,
  `IRepository<ItineraryItem>`, `IDestinationRepository`, `IUnitOfWork`.
- `DestinationService`: `IDestinationRepository` (plus its existing
  `IDestinationProvider`/`IImageSearchProvider` external-API dependencies,
  unchanged).

No behavior change is intended — this is a refactor of *how* data is
accessed, not what queries return or what validation/business rules run.
Existing FluentValidation validators and `AuthMappings`-style mapping classes
are untouched ([[always-use-validators-and-mappings]] convention continues
to apply to any new DTOs/mappings this touches).

## Tests

`AuthServiceTests.CreateDb()` (`backend/tests/TripPlanner.Application.Tests/Auth/AuthServiceTests.cs:24-27`)
keeps building a real in-memory `ApplicationDbContext` via
`UseInMemoryDatabase` — unchanged. `CreateSut` now wraps that context in a
real `UserRepository` + `UnitOfWork` instead of passing the context straight
into `AuthService`, so the repository/unit-of-work code itself is exercised
by the existing test suite, not bypassed. Any new repository-specific unit
tests (e.g. for `TripRepository.GetTripWithDetailsAsync`) follow the same
in-memory-db pattern, added under a new
`backend/tests/TripPlanner.Application.Tests/Repositories/` (or similar)
folder — exact location to be finalized in the implementation plan.

Per [[always-use-validators-and-mappings]] and this repo's testing
conventions, no mocking framework replaces the real EF Core in-memory
provider for data-access verification — only cross-cutting collaborators
(`IJwtTokenGenerator`, `IEmailSender`, etc.) are Moq-mocked.

## DI registration

`Infrastructure/DependencyInjection.cs`'s `AddPersistence` keeps registering
`ApplicationDbContext` as today, but no longer exposes it via
`IApplicationDbContext`. It additionally registers:

- `services.AddScoped(typeof(IRepository<>), typeof(Repository<>));` — open
  generic fallback, covers `IRepository<ItineraryDay>` and
  `IRepository<ItineraryItem>` directly.
- `services.AddScoped<IUserRepository, UserRepository>();`
- `services.AddScoped<ITripRepository, TripRepository>();`
- `services.AddScoped<IDestinationRepository, DestinationRepository>();`
- `services.AddScoped<IUnitOfWork, UnitOfWork>();`

`Application/DependencyInjection.cs`'s `AddApplication()` is unaffected —
it only registers the feature services and FluentValidation, which stays
exactly as-is.

## Documentation update

[TECHNICAL_SPEC.md:137-141](../../../TECHNICAL_SPEC.md) is updated to remove
the "no repository pattern" observed-deviation note and instead describe the
repository/unit-of-work layer as the current, intended data-access
abstraction.

## Out of scope

- No change to `DestinationService`'s upsert-cache semantics, `TripService`'s
  business rules, or any controller/DTO/validator.
- No Specification pattern, no generic `IQueryable` escape hatch on
  `IRepository<T>` — every non-trivial query gets a named method on a
  specific repository interface, by design (see "Query design" decision
  above).
- No transaction/`IDbContextTransaction` wrapper beyond what
  `SaveChangesAsync` already gives EF Core's default single-`SaveChanges`
  atomicity — out of scope unless a future flow needs multiple
  `SaveChangesAsync` calls to be atomic together.
