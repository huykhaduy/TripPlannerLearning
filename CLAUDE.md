# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project context

This is a .NET Full-Stack training capstone template (TripPlanner). `ASSIGNMENT.md` maps
the official feature list (Destination Suggestion, Destination Details, Trip Planner,
Auth) to user stories, priorities, and acceptance criteria per file — check it before
assuming a feature is unimplemented or out of scope. `README.md` has the getting-started
walkthrough and the "where to write your code" file map. `TECHNICAL_SPEC.md` is the
authoritative deep reference: Part A documents exactly what the current code does
(tagged `[Observed]`/`[Inferred]`), Part B is the full design spec, business rules, and
phase-by-phase implementation roadmap for the unimplemented features — consult it before
making a design decision (caching strategy, day-regeneration algorithm, sort-order
semantics, etc.) that isn't spelled out in ASSIGNMENT.md. Feature 4 (Auth) is the
complete reference slice; Destination Suggestion/Details and Trip Planner are now also
fully implemented (no remaining `NotImplementedException` stubs in `Application`) —
`ExceptionHandlingMiddleware`'s 501 mapping stays in place for any new feature work that
introduces one, but nothing currently throws it.

## Commands

### Backend (run from `backend/`)

```bash
dotnet restore
dotnet build
dotnet run --project src/TripPlanner.WebApi      # API at http://localhost:5080, Swagger at /swagger

# Run all tests
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~TripServiceTests"

# EF Core migrations (run from repo root or backend/)
dotnet ef migrations add <MigrationName> \
  --project src/TripPlanner.Infrastructure \
  --startup-project src/TripPlanner.WebApi
```

### Frontend (run from `frontend/`)

```bash
npm install
npm run dev        # Dev server at http://localhost:5173
npm run build      # Type-check + Vite build
npm run lint       # tsc --noEmit + ESLint (flat config in eslint.config.js)
npm test           # Vitest (single run); npm run test:watch for watch mode

# Run a single test file
npx vitest run src/auth/AuthContext.test.tsx
```

### Quick start (both servers, from repo root)

```bash
start-dev.bat      # Windows cmd
./dev.ps1          # PowerShell (two windows)
./dev.sh           # macOS/Linux
```

The scripts install frontend deps and create `frontend/.env` on first run.

## Architecture

### Clean Architecture layers (backend)

```
WebApi ──▶ Application ──▶ Domain
   │            ▲
   └──▶ Infrastructure ──┘
```

- **Domain** — entities (`Trip`, `ItineraryDay`, `ItineraryItem`, `Destination`, `User`). No framework dependencies.
- **Application** — service interfaces (`ITripService`, `IAuthService`, etc.) and implementations. Defines one repository interface per aggregate (`IUserRepository`, `ITripRepository`, `IDestinationRepository`) whose write methods save their own changes — there is no generic `IRepository<T>`, no unit of work, and no `IApplicationDbContext`. Also `IPasswordHasher`, `IJwtTokenGenerator`, `ICurrentUserService`, `IDestinationProvider`. Depends only on Domain.
- **Infrastructure** — EF Core `ApplicationDbContext`, repositories, JWT token generation, BCrypt, `GeoapifyClient`. Implements Application interfaces that are technology-specific but not tied to the HTTP pipeline.
- **WebApi** — controllers, middleware (`ExceptionHandlingMiddleware`), DI composition root. Registers all layers via `AddApplication()` / `AddInfrastructure()` / `AddWebApi()`. `AddWebApi()` (`WebApi/DependencyInjection.cs`) is the one exception to "Infrastructure implements Application interfaces": `ICurrentUserService` needs `IHttpContextAccessor`, an ASP.NET Core hosting concern, so its implementation (`WebApi/Services/CurrentUserService.cs`) and registration live in WebApi, not Infrastructure.

### Key patterns

- **DI registration**: each layer exposes an extension method (`AddApplication()`, `AddInfrastructure()`, `AddWebApi()`) — `Program.cs` stays small and just calls all three plus framework setup (auth, CORS, Swagger).
- **Current user**: inject `ICurrentUserService` and call `GetRequiredUserId()`. Never trust a resource ID alone — always filter by `UserId`.
- **Exceptions**: throw from the Application layer; `ExceptionHandlingMiddleware` maps them to HTTP status codes:
  | Exception | HTTP |
  |---|---|
  | `ValidationException` / `DomainException` | 400 |
  | `UnauthorizedException` | 401 |
  | `ForbiddenException` | 403 |
  | `NotFoundException` | 404 |
  | `ConflictException` | 409 |
  | `NotImplementedException` | 501 |
- **Validation**: every feature method starts by running its FluentValidation validator via
  `ValidateAndThrowAppExceptionAsync` (`Common/Validation/ValidationExtensions.cs`), which turns
  failures into our `ValidationException` → HTTP 400 with field-level `errors`. Validators are
  **not registered in DI and must not be** — `AddApplication()` deliberately has no
  `AddValidatorsFromAssembly` call, and the package is core `FluentValidation`, not
  `FluentValidation.DependencyInjectionExtensions`. Each service holds them as
  `private static readonly` instances (see `AuthService`), because they are stateless,
  dependency-free rule declarations: injecting `IValidator<T>` would be ten interfaces with one
  implementation each that nothing ever substitutes. The one thing that reverses this: a validator
  needing a dependency (e.g. an async rule hitting the database) can no longer be a static
  instance — convert *that* validator to a constructor parameter and leave the rest alone.
- **Logging**: adapters log at the source, then **rethrow**; callers keep their own fallback
  policy. `GeoapifyClient`, `SerperImageClient`, `SmtpEmailSender` and `ResilientDistributedCache`
  each catch their transient failures, log a Warning, and let the exception continue (the cache
  decorator is the exception — degrading to a miss *is* its contract, so it swallows after
  logging). Do not make an adapter swallow to "simplify" a caller: `DestinationService` decides
  whether to cache a result based on whether the search *failed* versus *found nothing*, and
  `AuthService`'s "registration survives a mail outage" guarantee (F4/US1) is asserted by a test
  at the Application level. `DestinationService` holds the only `ILogger` in Application, for the
  one failure that originates in this layer rather than an adapter: a cached entry that no longer
  matches its DTO shape.
- **Reference slice**: `AuthService.cs` + `AuthController.cs` are the canonical worked example. Study them before implementing other features.
- **Destination caching**: `Destination` rows are a cache of external-provider data (Geoapify), keyed by a unique index on `ProviderId` (`DestinationConfiguration.cs`). `DestinationService` (search/details) never persists rows — only `TripService.AddDestinationAsync` upserts one, on first add to any trip. Do not reintroduce a "must already exist" lookup there; a fresh `ProviderId` is expected and should upsert, not 404.
- **Entities supply their own keys**: `BaseEntity` self-assigns `Id = Guid.NewGuid()`, so all five
  entity configurations declare `builder.Property(x => x.Id).ValueGeneratedNever()`. Without it, EF's
  convention treats a Guid key as store-generated and classifies a *new* child arriving with a key
  already set as an EXISTING row — issuing an UPDATE instead of an INSERT, which surfaces as
  `DbUpdateConcurrencyException: Attempted to update or delete an entity that does not exist`. This
  is what lets `TripService` just do `trip.Days.Add(...)` / `trip.Items.Add(...)` and rely on one
  `UpdateAsync`. The `SetIdValueGeneratedNever` migration is intentionally **empty** and must not be
  deleted: it carries the metadata change into `ApplicationDbContextModelSnapshot`, which later
  migrations diff against.
- **Concurrency via unique index + catch/retry**: this codebase enforces invariants that matter under concurrent requests as DB-level unique indexes (`User.Email`, `Destination.ProviderId`, `(ItineraryDayId, DestinationId)`) rather than app-level locking. `ApplicationDbContext.SaveChangesAsync` translates a Postgres unique violation into `ConcurrencyException`, so read-then-write code catches that (never EF's `DbUpdateException`) and re-fetches or re-reports — all three indexes have a handler: `AuthService.RegisterAsync` (→ the same generic 409 as the in-memory duplicate check, so the two can't be told apart), `TripService.GetOrCreateDestinationAsync`, and `TripService.SaveWithDuplicateGuardAsync`. `DestinationRepository.AddAsync` detaches its losing entity before rethrowing so the caller can re-fetch on a clean change tracker. EF Core's InMemory provider (used in tests) does **not** enforce these indexes, so the translation itself is untested by `dotnet test` and must be reasoned about directly against the SQL provider; the *callers* are tested by simulating `ConcurrencyException` at the repository boundary with a mock (see `AuthServiceTests`).
- **Two accepted concurrency gaps**, both decisions rather than oversights. Postgres treats every
  NULL as distinct, so `(ItineraryDayId, DestinationId)` does **not** cover Saved Places rows
  (`ItineraryDayId IS NULL`) — two concurrent adds of the same place to one trip's Saved Places can
  both succeed. A filtered unique index for it was written and then deliberately reverted as
  over-engineering for this project; `TripService`'s in-memory check is the only guard, and the
  exposure is one duplicate row in a user's own trip. Separately, `ItineraryItem.SortOrder` has no
  unique index — ordering is enforced purely in-memory via dense resequencing per request
  (`TripService.Resequence`), so two concurrent reorders of the same bucket race.

### Feature layout (backend)

```
Application/Features/
  Auth/               ← reference implementation
  Destinations/       ← DestinationService.cs
  Trips/              ← TripService.cs
```

All three are fully implemented (Auth is the canonical worked example to study first, not the only complete one).

Each feature folder holds: the service implementation, an interface, and a `Dtos/` subfolder.

### Frontend structure

```
src/
  api/         ← axios wrappers: client.ts (base instance), auth.ts, destinations.ts, trips.ts
  auth/        ← AuthContext (JWT storage, reactive logout on 401), ProtectedRoute
  features/    ← page components grouped by feature (auth, destinations, trips — all implemented)
  types.ts     ← shared TypeScript types for every DTO (auth, destination, trip)
  App.tsx      ← React Router route definitions
```

HTTP client (`src/api/client.ts`) is a configured Axios instance whose JWT interceptor
attaches `Authorization: Bearer <token>` when present, and exposes two shared error helpers:
`getErrorMessage(err, fallback)` for the backend's `ProblemDetails.detail`, and
`getErrorStatus(err)` for the HTTP status when a *specific* code changes the UI rather than just
the message (e.g. 404 → render a "not found" state). **`src/api/` is the only place allowed to
import `axios`** — components call these helpers instead of narrowing the error type themselves,
so the HTTP library stays swappable and out of the presentation layer.
`destinations.ts` and `trips.ts`
follow `auth.ts`'s pattern: thin typed wrappers over `apiClient`, no raw `fetch`/`axios`
calls in components.

**Frontend tests** run on Vitest + React Testing Library in a `jsdom` environment,
configured in the `test` block of `vite.config.ts` with `src/test/setup.ts` as the setup
file. `globals` is deliberately **false**: test files import `describe`/`it`/`expect` from
`vitest` explicitly, which means the existing `tsconfig.json` (whose `include` already
covers `src`) type-checks them with no `types` entry — so `npm run lint` checks the tests
too, and a wrong mock signature fails the build rather than passing silently. Because
globals are off, RTL cannot register its own `afterEach`, so setup.ts calls `cleanup()`
**and** `localStorage.clear()` by hand — `AuthProvider` reads localStorage during its
initial render, so a leftover session would leak into the next test.

Covered so far: the `client.ts` error helpers, `AuthProvider` (login/logout persistence,
refresh restore, the reactive-logout listener, and that register does *not* start a
session), `ProtectedRoute`'s redirect, and three feature pages — `LoginPage` (the 403 →
resend-verification branch, and returning to the page that sent the user there),
`TripsPage` (list/empty/load-error, the create-trip modal, and the F3/US10 status pill),
and `DestinationDetailsPage` (404 → "not found" versus a retryable failure).

Conventions these follow:

- API modules are mocked wholesale with `vi.mock('../../api/<module>', ...)` — list every
  export, since a partial factory makes the missing ones `undefined` at call time.
- "Already signed in" is expressed by seeding `localStorage['tripplanner.user']` **before**
  render, because that is what `AuthProvider` reads during its initial render.
- Build axios rejections with `httpError(status, body)` / `networkError()` from
  `src/test/http.ts` rather than hand-rolled objects, so `getErrorMessage`/`getErrorStatus`
  take their real branches.
- Date-dependent assertions (the status pill) are written **relative to today** via an
  offset helper, so they cannot rot into failures on a future date.
- `DestinationDetailsPage` needs an `AuthProvider` wrapper for its success path only —
  `AddToTripButton` calls `useAuth`, and the error paths return before rendering it.

Still untested: `TripDetailPage` (the largest page — day scheduling, reordering, the edit
form), `RegisterPage`, `VerifyEmailPage`, `SearchPage`, and the destination sub-components
(`CitySearchInput`, `AttractionsList`, `NearbyAttractions`, `AddToTripButton`). `types.ts` covers `User`/`AuthResponse` plus every destination/trip
DTO consumed by the feature pages — add new shared shapes here rather than declaring
ad-hoc inline interfaces in components.

### Testing pattern (xUnit)

Tests use an **in-memory EF Core database** (new `Guid` database name per test) and **Moq** for interfaces. See `AuthServiceTests.cs` as the reference:

```csharp
private static ApplicationDbContext CreateDb() =>
    new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options);
```

Arrange with a fresh `CreateDb()`, construct the service under test with real implementations where cheap (e.g. `BCryptPasswordHasher`) and `Mock<T>` for everything else. Services take no validator
arguments (they hold their own), and anything needing an `ILogger` gets
`NullLogger<T>.Instance`.

`*ValidatorTests` (one per feature) test the validators directly with FluentValidation's
`TestValidate` helper. They exist for what the service tests don't check: which **property**
carries the error, the exact **message** (several are deliberately identical so they can't be used
to probe whether an account exists), and the **boundary** either side of each limit. Three of them
pin deliberate *absences* — `Login_WithAPasswordShorterThanRegisterAllows_IsStillValid` (adding a
length rule to login would lock out accounts whose passwords predate a policy change),
`UpdateTrip_WithEndBeforeStart_IsNotTheValidatorsJob` (that's `Trip.SetDates`, a domain rule), and
`UpdateItem_WithAPositionPastTheEndOfTheBucket_IsValid` (`TripService` clamps, so "99" means
"last"). Watch the trip-length rule: it compares a *difference* of day numbers with a strict `<`,
so the largest accepted range is `MaxTripLengthDays - 1` days apart, i.e. 365 days counted
inclusively.

`ApplicationDbContextTests` covers the `SaveChangesAsync` override's audit stamping — the
unique-violation translation in the same method is not reachable under InMemory.

`TripPlanner.WebApi.Tests` covers what the above can't: real HTTP routing, `[Authorize]`
enforcement, query-string binding, and `ExceptionHandlingMiddleware`'s exception-to-status-code
mapping. Uses
`WebApplicationFactory<Program>` against the real `Program` (see `CustomWebApplicationFactory.cs`),
with `ApplicationDbContext` swapped from Npgsql to a fresh EF Core InMemory database per
factory instance. External providers are replaced by `FakeExternalProviders.cs`, whose
`FakeDestinationProvider` answers **every** id with a hit *except* ids prefixed
`FakeDestinationProvider.UnknownIdPrefix` (`"unknown-"`) — that prefix is the only way to reach the
"unknown to the provider AND absent from the database" 404 branch. `DestinationsEndpointsTests`
also pins that the destinations endpoints stay **public** (no `[Authorize]`, F3/US8): a stray
attribute there would break anonymous browsing and nothing else in the suite would notice.

Two things to know before touching the factory:

- `AddDbContext`'s `optionsLifetime` defaults to **Scoped**, not Singleton — the InMemory
  database name must be generated *once* and captured (a field), never inlined as
  `Guid.NewGuid()` directly in the options lambda, or every DI scope (every request, and
  every `factory.Services.CreateScope()` a test opens) gets its own empty database.
- Removing `DbContextOptions<ApplicationDbContext>` alone isn't enough to swap providers —
  `AddDbContext` also registers the configuring action itself as a composable
  `IDbContextOptionsConfiguration<T>` (so multiple `AddDbContext` calls layer instead of
  replacing each other); leaving that behind means Program's `UseNpgsql(...)` and the
  test's `UseInMemoryDatabase(...)` both apply to the same options, and EF Core throws.
  Remove both.

`Program.cs`'s automatic migration-on-startup is skipped when `app.Environment.IsEnvironment("Testing")`
(set via `builder.UseEnvironment("Testing")` in the factory) — the InMemory provider doesn't
support migrations at all. `Jwt:Key` is supplied via `builder.UseSetting(...)`, which wins
over the process environment variables `DotNetEnv.Env.Load()` creates from a developer's
local `.env` — `TestHostConfigurationTests` pins that precedence, so the suite can never
silently start signing tokens with someone's real key. No secret needs to be an environment
variable, because nothing is read eagerly during startup any more: `Jwt` is bound and
validated as options in `AddInfrastructure` (`ValidateOnStart`), and the Postgres connection
string is only read inside `AddPersistence`, whose registration the factory replaces
outright. The email-verification
flow is driven by minting a token directly via `IJwtTokenGenerator` (resolved from
`factory.Services`) rather than intercepting a real email — see `AuthTestHelper.cs`.

## Configuration

- **There is no `appsettings.json`/`appsettings.Development.json`** — all configuration is either a
  C# default baked into a settings class (`JwtSettings`, `SmtpSettings`, `GeoapifySettings`,
  `SerperSettings`, all in `TripPlanner.Infrastructure`) or an override in the git-ignored
  `backend/src/TripPlanner.WebApi/.env`, loaded via `DotNetEnv.Env.Load()` in `Program.cs` before
  `WebApplication.CreateBuilder` runs. Copy `.env.example` to `.env` and fill in real values.
  Nested config keys use `__` as the separator (e.g. `Geoapify__ApiKey` → `Geoapify:ApiKey`),
  since `.env` values become process environment variables and ASP.NET Core's
  `AddEnvironmentVariables()` treats `__` as the section delimiter; array elements use a
  trailing index (`Cors__AllowedOrigins__0`).
- API URL: `http://localhost:5080`; frontend `.env` → `VITE_API_BASE_URL=http://localhost:5080/api`
- JWT: `Jwt__Issuer`/`Jwt__Audience`/`Jwt__ExpiryMinutes` are optional overrides (defaults live in
  `JwtSettings.cs`: `TripPlanner`/`TripPlannerClient`/`60`). `Jwt__Key` is the one required,
  no-default value — a defaulted signing key would defeat JWT security, so `AddInfrastructure`
  registers it with `.Validate(...).ValidateOnStart()` (blank, and shorter than
  `JwtSettings.MinKeyBytes`) and the host refuses to start rather than falling back, unlike the
  URL fallbacks below. Validation lives in the options registration, not in `Program.cs`: an
  eager `builder.Configuration[...]` read runs before any test host can layer in its own value.
- Database is PostgreSQL only (no SQLite/InMemory fallback outside tests) — required, no default:
  run `docker compose up -d` and set `ConnectionStrings__Postgres` in `.env` (see `.env.example`).
- Destination browse-path caching (`DestinationService`) defaults to an in-process `IDistributedCache`.
  Switch to Redis by setting `Cache__Provider=Redis` in `.env` and running `docker compose up -d`;
  the connection string comes from `ConnectionStrings__Redis` in the same file.
- Migrations apply automatically on startup via `ApplyMigrationsAsync` in `Program.cs`.
- Geoapify/Serper API keys and SMTP credentials are required secrets with no default (empty string
  in the settings classes) — must come from `.env`. `Cors__AllowedOrigins__0`, `App__FrontendBaseUrl`,
  `Geoapify__BaseUrl`, `Serper__BaseUrl`, `Smtp__Host`/`Smtp__Port` all have a matching
  `?? "localhost default"` (or class-level default) in code, so the app still runs with an empty
  `.env` for those — see `AppUrlProvider.cs`, `DependencyInjection.cs` (Geoapify/Serper `HttpClient`
  setup), `SmtpSettings.cs`, and `Program.cs`'s CORS policy.
